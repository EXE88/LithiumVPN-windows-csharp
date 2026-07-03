using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Lithiumvpn.Services.Xray
{
    public enum VpnState { Disconnected, Connecting, Connected }

    /// <summary>
    /// App-wide VPN connection orchestrator.
    ///
    /// Connect: parse the share link → build the Xray config → start the core →
    /// verify the tunnel actually passes traffic → enable the system proxy.
    /// Disconnect reverses it. All transitions are serialized by one gate so
    /// rapid clicks / config switches can't interleave.
    /// </summary>
    public sealed class ConnectionService
    {
        private static readonly Lazy<ConnectionService> _lazy = new(() => new ConnectionService());
        public static ConnectionService Instance => _lazy.Value;

        private readonly XrayProcess _xray = new();
        private readonly SemaphoreSlim _gate = new(1, 1);

        private ConnectionService()
        {
            _xray.Exited += OnCoreDied;
        }

        public VpnState State { get; private set; } = VpnState.Disconnected;
        public int SocksPort { get; private set; }
        public int HttpPort { get; private set; }
        public int MetricsPort { get; private set; }
        public string? ActiveConfigCode { get; private set; }
        public string? ActiveAddress { get; private set; }
        public int ActivePort { get; private set; }

        /// <summary>Raised on every state transition (may fire on a background thread).</summary>
        public event Action<VpnState>? StateChanged;

        /// <summary>
        /// One-time startup cleanup: kill cores orphaned by a crash and undo a
        /// system proxy a crashed session left behind.
        /// </summary>
        public static void CleanupFromPreviousRun()
        {
            XrayProcess.KillOrphans();
            SystemProxyService.RestoreIfMarkerExists();
        }

        public async Task ConnectAsync(string configCode, CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct);
            try
            {
                // Parse first — a bad link should fail before we tear anything down.
                var parsed = XrayLinkParser.Parse(configCode);

                TearDownCore();
                SetState(VpnState.Connecting);

                try
                {
                    SocksPort = XrayProcess.GetFreePort();
                    HttpPort = XrayProcess.GetFreePort();
                    MetricsPort = XrayProcess.GetFreePort();

                    var json = XrayConfigBuilder.Build(parsed.Outbound, SocksPort, HttpPort, MetricsPort);
                    await _xray.StartAsync(json, SocksPort, ct);

                    // Prove the tunnel passes real traffic before flipping the system proxy.
                    var latency = await NetworkTestService.ProxiedLatencyAsync(
                        HttpPort, TimeSpan.FromSeconds(12), ct);
                    if (latency < 0)
                        throw new InvalidOperationException(
                            "Tunnel established but no traffic passes through the server.");

                    SystemProxyService.Enable(HttpPort);

                    ActiveConfigCode = configCode;
                    ActiveAddress = parsed.Address;
                    ActivePort = parsed.Port;
                    SetState(VpnState.Connected);
                }
                catch
                {
                    TearDownCore();
                    SetState(VpnState.Disconnected);
                    throw;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        public async Task DisconnectAsync()
        {
            await _gate.WaitAsync();
            try
            {
                TearDownCore();
                SetState(VpnState.Disconnected);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Synchronous best-effort teardown for window-close / process-exit paths
        /// where async continuations can no longer run.
        /// </summary>
        public void ShutdownBlocking()
        {
            try { SystemProxyService.Disable(); } catch { }
            try { _xray.Stop(); } catch { }
            State = VpnState.Disconnected;
        }

        private void TearDownCore()
        {
            try { SystemProxyService.Disable(); } catch { }
            _xray.Stop();
            ActiveConfigCode = null;
            ActiveAddress = null;
            ActivePort = 0;
            MetricsPort = 0;
        }

        private void OnCoreDied()
        {
            // The core crashed / was killed externally while we thought we were up.
            if (State != VpnState.Connected && State != VpnState.Connecting) return;
            try { SystemProxyService.Disable(); } catch { }
            ActiveConfigCode = null;
            ActiveAddress = null;
            ActivePort = 0;
            SetState(VpnState.Disconnected);
        }

        private void SetState(VpnState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        /// <summary>HttpClient handler routed through the active tunnel (HTTP inbound).</summary>
        public HttpClientHandler CreateProxiedHandler() => new()
        {
            Proxy = new WebProxy($"http://127.0.0.1:{HttpPort}"),
            UseProxy = true
        };

        /// <summary>
        /// Re-applies the system proxy so a changed bypass/exception list takes
        /// effect immediately while connected. No-op when disconnected.
        /// </summary>
        public void ReapplyProxyIfConnected()
        {
            if (State == VpnState.Connected)
            {
                try { SystemProxyService.Enable(HttpPort); } catch { }
            }
        }

        // ─── Traffic counters (Xray stats via the metrics endpoint) ───
        private static readonly HttpClient _statsHttp = new(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        /// <summary>
        /// Cumulative bytes moved through the proxy outbound since the tunnel
        /// started, or <c>null</c> when unavailable.
        /// </summary>
        public async Task<(long Uplink, long Downlink)?> GetTrafficAsync()
        {
            if (State != VpnState.Connected || MetricsPort == 0) return null;
            try
            {
                var raw = await _statsHttp.GetStringAsync($"http://127.0.0.1:{MetricsPort}/debug/vars");
                using var doc = System.Text.Json.JsonDocument.Parse(raw);
                var proxy = doc.RootElement
                    .GetProperty("stats").GetProperty("outbound").GetProperty("proxy");
                return (proxy.GetProperty("uplink").GetInt64(),
                        proxy.GetProperty("downlink").GetInt64());
            }
            catch { return null; }
        }
    }
}
