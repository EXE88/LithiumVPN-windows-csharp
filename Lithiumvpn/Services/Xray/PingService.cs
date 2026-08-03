using System;
using System.Collections.Generic;
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
    /// Strictly single-flight: starting a new ping (single or batch) cancels the one
    /// in progress, and every probe runs inside one semaphore, so at most one probe
    /// core is ever alive regardless of how many buttons or dialogs ask at once.
    /// Batches are therefore run SEQUENTIALLY — firing them in parallel would both
    /// supersede each other and leave several cores fighting over the same ports.
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

        /// <summary>
        /// The probe core currently alive, if any. Tracked only so app shutdown can
        /// kill it synchronously: a batch keeps a core up far longer than a single
        /// ping, so without this an exit mid-batch would orphan an xray.exe until the
        /// next launch's <see cref="XrayProcess.KillOrphans"/> swept it up.
        /// </summary>
        private XrayProcess? _activeCore;

        private PingService() { }

        /// <summary>
        /// Best-effort synchronous teardown for window-close / process-exit paths,
        /// where async continuations can no longer run.
        /// </summary>
        public void ShutdownBlocking()
        {
            lock (_lock)
            {
                try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
            }
            try { Volatile.Read(ref _activeCore)?.Stop(); } catch { }
        }

        /// <summary>
        /// Pings the given config through a temporary tunnel.
        /// </summary>
        /// <returns>
        /// Latency in ms; <c>-1</c> when the probe failed; <c>null</c> when this
        /// ping was superseded by a newer request (caller should not render it).
        /// </returns>
        public async Task<int?> PingConfigAsync(string configCode)
        {
            var cts = BeginOperation();
            try
            {
                return await ProbeOnceAsync(configCode, cts);
            }
            finally
            {
                EndOperation(cts);
            }
        }

        /// <summary>
        /// Pings a whole group of configs one after another, reporting each result as
        /// it arrives so the UI can fill in tags progressively.
        ///
        /// The callback runs on a background thread — marshal to the UI thread inside it.
        /// Returns when every config was probed, or early if the batch was cancelled by
        /// the caller or superseded by a newer ping request.
        /// </summary>
        /// <param name="configCodes">Share links / config codes to probe, in order.</param>
        /// <param name="onResult">Invoked per config with (index, latency-in-ms, -1 on failure).</param>
        /// <param name="ct">Caller's cancellation (e.g. the page navigating away).</param>
        public async Task PingBatchAsync(
            IReadOnlyList<string> configCodes,
            Action<int, int> onResult,
            CancellationToken ct = default)
        {
            if (configCodes is null || configCodes.Count == 0) return;

            var cts = BeginOperation(ct);
            try
            {
                for (int i = 0; i < configCodes.Count; i++)
                {
                    if (cts.IsCancellationRequested) return;

                    var result = await ProbeOnceAsync(configCodes[i], cts);
                    if (result is null) return;   // superseded / cancelled → stop the batch

                    try { onResult(i, result.Value); }
                    catch { /* a UI callback must never break the batch */ }
                }
            }
            finally
            {
                EndOperation(cts);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Single-flight bookkeeping
        // ─────────────────────────────────────────────────────────────

        /// <summary>Supersedes any in-flight ping and registers this one as current.</summary>
        private CancellationTokenSource BeginOperation(CancellationToken external = default)
        {
            var cts = external.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(external)
                : new CancellationTokenSource();

            lock (_lock)
            {
                // Safe: the previous CTS is only disposed while holding this lock,
                // so it can never be disposed underneath this Cancel() call.
                try { _cts?.Cancel(); } catch (ObjectDisposedException) { }
                _cts = cts;
            }
            return cts;
        }

        private void EndOperation(CancellationTokenSource cts)
        {
            lock (_lock)
            {
                if (ReferenceEquals(_cts, cts)) _cts = null;
                cts.Dispose();
            }
        }

        /// <summary>
        /// Runs one probe under the gate. Returns null when superseded/cancelled so the
        /// caller knows not to render (and, for a batch, to stop).
        /// </summary>
        private async Task<int?> ProbeOnceAsync(string configCode, CancellationTokenSource cts)
        {
            try { await _gate.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { return null; }

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
            }
        }

        private async Task<int> RunProbeAsync(string configCode, CancellationToken ct)
        {
            XrayLinkParser.ParsedConfig parsed;
            try { parsed = XrayLinkParser.Parse(configCode); }
            catch { return -1; }

            // `using` + the explicit Stop() below guarantee the temp core dies on every
            // path — success, probe failure, supersede, or an unexpected throw.
            using var core = new XrayProcess { ConfigFileName = "ping-config.json" };
            Volatile.Write(ref _activeCore, core);
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
                Volatile.Write(ref _activeCore, null);
                core.Stop();
            }
        }
    }
}
