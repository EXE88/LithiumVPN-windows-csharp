using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Lithiumvpn.Services.Xray
{
    /// <summary>
    /// Measures a config's real-world latency by spinning up a temporary Xray core
    /// (own ports, own config file — the main tunnel is untouched) and timing an
    /// HTTP request to Google's 204 endpoint through it.
    ///
    /// Strictly single-flight: starting a new ping cancels the one in progress,
    /// so at most one probe core is ever alive regardless of how many buttons or
    /// dialogs ask for pings at once.
    /// </summary>
    public sealed class PingService
    {
        private static readonly Lazy<PingService> _lazy = new(() => new PingService());
        public static PingService Instance => _lazy.Value;

        /// <summary>Configurable via .env (PING_URL); expects a 204/2xx response.</summary>
        private static string ProbeUrl => AppConfig.PingUrl;
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(12);

        private readonly SemaphoreSlim _gate = new(1, 1);
        private CancellationTokenSource? _cts;
        private readonly object _lock = new();

        private PingService() { }

        /// <summary>
        /// Pings the given config through a temporary tunnel.
        /// </summary>
        /// <returns>
        /// Latency in ms; <c>-1</c> when the probe failed; <c>null</c> when this
        /// ping was superseded by a newer request (caller should not render it).
        /// </returns>
        public async Task<int?> PingConfigAsync(string configCode)
        {
            CancellationTokenSource cts;
            lock (_lock)
            {
                _cts?.Cancel();          // supersede any in-flight probe
                cts = new CancellationTokenSource();
                _cts = cts;
            }

            // Serialize actual probe work so only one temp core exists at a time.
            await _gate.WaitAsync();
            try
            {
                if (cts.IsCancellationRequested) return null;
                return await RunProbeAsync(configCode, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            finally
            {
                _gate.Release();
                lock (_lock)
                {
                    if (ReferenceEquals(_cts, cts)) _cts = null;
                }
                cts.Dispose();
            }
        }

        private static async Task<int> RunProbeAsync(string configCode, CancellationToken ct)
        {
            XrayLinkParser.ParsedConfig parsed;
            try { parsed = XrayLinkParser.Parse(configCode); }
            catch { return -1; }

            using var core = new XrayProcess { ConfigFileName = "ping-config.json" };
            try
            {
                int socksPort = XrayProcess.GetFreePort();
                int httpPort = XrayProcess.GetFreePort();
                var json = XrayConfigBuilder.Build(parsed.Outbound, socksPort, httpPort);

                await core.StartAsync(json, httpPort, ct);

                using var http = new HttpClient(new HttpClientHandler
                {
                    Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
                    UseProxy = true
                })
                { Timeout = ProbeTimeout };

                var sw = Stopwatch.StartNew();
                using var response = await http.GetAsync(ProbeUrl, ct);
                sw.Stop();

                return (int)response.StatusCode == 204 || response.IsSuccessStatusCode
                    ? (int)sw.ElapsedMilliseconds
                    : -1;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // superseded — bubble up so the caller gets null
            }
            catch
            {
                return -1;
            }
            finally
            {
                core.Stop();
            }
        }
    }
}
