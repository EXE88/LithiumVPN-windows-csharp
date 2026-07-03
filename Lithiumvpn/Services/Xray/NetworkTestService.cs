using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Lithiumvpn.Services.Xray
{
    /// <summary>
    /// Real network measurements:
    ///  • <see cref="TcpPingAsync"/> — TCP handshake time to a server endpoint
    ///    (works where ICMP is filtered; used by the per-config Ping buttons).
    ///  • <see cref="ProxiedLatencyAsync"/> — end-to-end latency through the
    ///    tunnel (HTTP 204 probe), shown on the dashboard while connected.
    ///  • <see cref="MeasureDownloadAsync"/> / <see cref="MeasureUploadAsync"/> —
    ///    throughput through the tunnel against Cloudflare's speed endpoints.
    /// </summary>
    public static class NetworkTestService
    {
        /// <summary>Configurable via .env (TUNNEL_CHECK_URL).</summary>
        private static string LatencyProbeUrl => AppConfig.TunnelCheckUrl;
        private const string DownloadUrl = "https://speed.cloudflare.com/__down?bytes=25000000";
        private const string UploadUrl = "https://speed.cloudflare.com/__up";

        // ─────────────────────────────────────────────────────────
        //  TCP ping (direct, to the config's server endpoint)
        // ─────────────────────────────────────────────────────────
        /// <returns>Round-trip in ms, or -1 when unreachable.</returns>
        public static async Task<int> TcpPingAsync(string host, int port, int timeoutMs = 5000)
        {
            try
            {
                using var client = new TcpClient();
                var sw = Stopwatch.StartNew();
                var connect = client.ConnectAsync(host, port);
                var done = await Task.WhenAny(connect, Task.Delay(timeoutMs));
                if (done != connect || !client.Connected) return -1;
                sw.Stop();
                await connect; // surface exceptions
                return (int)sw.ElapsedMilliseconds;
            }
            catch { return -1; }
        }

        // ─────────────────────────────────────────────────────────
        //  Latency through the tunnel
        // ─────────────────────────────────────────────────────────
        /// <returns>Latency in ms, or -1 on failure.</returns>
        public static async Task<int> ProxiedLatencyAsync(
            int httpPort, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            using var http = CreateProxiedClient(httpPort, timeout ?? TimeSpan.FromSeconds(8));
            try
            {
                // First request warms the tunnel (TLS + transport handshakes);
                // the second one measures steady-state latency.
                using (var warm = await http.GetAsync(LatencyProbeUrl, ct)) { }

                var sw = Stopwatch.StartNew();
                using var response = await http.GetAsync(LatencyProbeUrl, ct);
                sw.Stop();
                return response.IsSuccessStatusCode || (int)response.StatusCode == 204
                    ? (int)sw.ElapsedMilliseconds
                    : -1;
            }
            catch { return -1; }
        }

        // ─────────────────────────────────────────────────────────
        //  Throughput
        // ─────────────────────────────────────────────────────────
        /// <summary>
        /// Downloads through the tunnel for up to <paramref name="maxSeconds"/>,
        /// reporting instantaneous Mbps via <paramref name="progress"/>.
        /// </summary>
        /// <returns>Average speed in Mbps, or -1 on failure.</returns>
        public static async Task<double> MeasureDownloadAsync(
            int httpPort, IProgress<double>? progress = null,
            int maxSeconds = 8, CancellationToken ct = default)
        {
            using var http = CreateProxiedClient(httpPort, TimeSpan.FromSeconds(maxSeconds + 15));
            try
            {
                using var response = await http.GetAsync(
                    DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                var buffer = new byte[64 * 1024];
                long totalBytes = 0;
                var total = Stopwatch.StartNew();
                var window = Stopwatch.StartNew();
                long windowBytes = 0;

                while (total.Elapsed.TotalSeconds < maxSeconds)
                {
                    int read = await stream.ReadAsync(buffer, ct);
                    if (read <= 0) break;
                    totalBytes += read;
                    windowBytes += read;

                    if (window.ElapsedMilliseconds >= 500)
                    {
                        progress?.Report(ToMbps(windowBytes, window.Elapsed));
                        windowBytes = 0;
                        window.Restart();
                    }
                }

                total.Stop();
                return totalBytes == 0 ? -1 : ToMbps(totalBytes, total.Elapsed);
            }
            catch { return -1; }
        }

        /// <summary>
        /// Uploads random data through the tunnel, reporting instantaneous Mbps.
        /// </summary>
        /// <returns>Average speed in Mbps, or -1 on failure.</returns>
        public static async Task<double> MeasureUploadAsync(
            int httpPort, IProgress<double>? progress = null,
            int maxSeconds = 8, CancellationToken ct = default)
        {
            using var http = CreateProxiedClient(httpPort, TimeSpan.FromSeconds(maxSeconds + 15));
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(maxSeconds));

                var content = new MeteredStreamContent(maxSeconds, progress, timeoutCts.Token);
                using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = content };

                var total = Stopwatch.StartNew();
                try
                {
                    using var response = await http.SendAsync(request, ct);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // expected: we cut the upload off after maxSeconds
                }
                total.Stop();

                return content.BytesSent == 0 ? -1 : ToMbps(content.BytesSent, total.Elapsed);
            }
            catch { return -1; }
        }

        private static double ToMbps(long bytes, TimeSpan elapsed) =>
            elapsed.TotalSeconds <= 0 ? 0 : bytes * 8 / elapsed.TotalSeconds / 1_000_000;

        private static HttpClient CreateProxiedClient(int httpPort, TimeSpan timeout) => new(
            new HttpClientHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{httpPort}"),
                UseProxy = true
            })
        { Timeout = timeout };

        /// <summary>
        /// Streams pseudo-random data for a bounded duration while counting the
        /// bytes that actually left, so the average reflects real throughput.
        /// </summary>
        private sealed class MeteredStreamContent : HttpContent
        {
            private readonly int _maxSeconds;
            private readonly IProgress<double>? _progress;
            private readonly CancellationToken _cutoff;

            public long BytesSent { get; private set; }

            public MeteredStreamContent(int maxSeconds, IProgress<double>? progress, CancellationToken cutoff)
            {
                _maxSeconds = maxSeconds;
                _progress = progress;
                _cutoff = cutoff;
                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            }

            protected override async Task SerializeToStreamAsync(
                System.IO.Stream stream, TransportContext? context)
            {
                var buffer = new byte[64 * 1024];
                new Random().NextBytes(buffer);

                var total = Stopwatch.StartNew();
                var window = Stopwatch.StartNew();
                long windowBytes = 0;

                while (total.Elapsed.TotalSeconds < _maxSeconds && !_cutoff.IsCancellationRequested)
                {
                    await stream.WriteAsync(buffer, _cutoff);
                    BytesSent += buffer.Length;
                    windowBytes += buffer.Length;

                    if (window.ElapsedMilliseconds >= 500)
                    {
                        _progress?.Report(ToMbps(windowBytes, window.Elapsed));
                        windowBytes = 0;
                        window.Restart();
                    }
                }
            }

            protected override bool TryComputeLength(out long length)
            {
                length = -1;
                return false;
            }
        }
    }
}
