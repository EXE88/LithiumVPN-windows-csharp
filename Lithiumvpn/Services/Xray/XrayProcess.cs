using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lithiumvpn.Services.Xray
{
    /// <summary>
    /// Owns the xray.exe child process: locates the binary shipped in
    /// <c>&lt;app&gt;\Xray</c>, writes the generated config under
    /// <c>%LOCALAPPDATA%\Lithiumvpn\xray</c>, starts/stops the core, and
    /// captures its output for diagnostics.
    /// </summary>
    public sealed class XrayProcess : IDisposable
    {
        private Process? _process;
        private readonly StringBuilder _log = new();
        private readonly object _lock = new();

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    try { return _process is { HasExited: false }; }
                    catch { return false; }
                }
            }
        }

        /// <summary>Raised when the core dies on its own (crash / killed externally).</summary>
        public event Action? Exited;

        public string RecentLog
        {
            get { lock (_lock) return _log.ToString(); }
        }

        public static string XrayDirectory => Path.Combine(AppContext.BaseDirectory, "Xray");
        public static string XrayExePath => Path.Combine(XrayDirectory, "xray.exe");

        public static string WorkDirectory
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Lithiumvpn", "xray");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string ConfigPath => Path.Combine(WorkDirectory, "config.json");

        /// <summary>Finds a free loopback TCP port.</summary>
        public static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <summary>
        /// Writes the config and launches the core, then waits until the given local
        /// port is accepting connections. Throws with the captured core log on failure.
        /// </summary>
        public async Task StartAsync(string configJson, int probePort, CancellationToken ct = default)
        {
            Stop();

            if (!File.Exists(XrayExePath))
                throw new FileNotFoundException($"xray.exe not found at {XrayExePath}");

            File.WriteAllText(ConfigPath, configJson);

            var psi = new ProcessStartInfo
            {
                FileName = XrayExePath,
                Arguments = $"run -c \"{ConfigPath}\"",
                WorkingDirectory = XrayDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            // Point the core at the geo assets shipped next to the binary.
            psi.Environment["XRAY_LOCATION_ASSET"] = XrayDirectory;

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => AppendLog(e.Data);
            process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
            process.Exited += (_, _) =>
            {
                bool wasOurs;
                lock (_lock) wasOurs = ReferenceEquals(_process, process);
                if (wasOurs) Exited?.Invoke();
            };

            lock (_lock)
            {
                _log.Clear();
                _process = process;
            }

            if (!process.Start())
                throw new InvalidOperationException("Failed to start xray.exe.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for the inbound port to come up (or the core to die with an error).
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();

                if (process.HasExited)
                    throw new InvalidOperationException(
                        $"Xray core exited immediately:\n{TrimLog(RecentLog)}");

                if (await IsPortOpenAsync(probePort))
                    return;

                await Task.Delay(150, ct);
            }

            Stop();
            throw new TimeoutException(
                $"Xray core did not open its local port in time.\n{TrimLog(RecentLog)}");
        }

        public void Stop()
        {
            Process? p;
            lock (_lock)
            {
                p = _process;
                _process = null;
            }
            if (p is null) return;

            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch { /* already gone */ }
            finally
            {
                try { p.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Kills any xray.exe left over from a previous crashed session
        /// (only processes running from our own install directory).
        /// </summary>
        public static void KillOrphans()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("xray"))
                {
                    try
                    {
                        var path = p.MainModule?.FileName;
                        if (string.Equals(path, XrayExePath, StringComparison.OrdinalIgnoreCase))
                            p.Kill(entireProcessTree: true);
                    }
                    catch { /* access denied / already exited */ }
                    finally { p.Dispose(); }
                }
            }
            catch { }
        }

        private static async Task<bool> IsPortOpenAsync(int port)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.ConnectAsync(IPAddress.Loopback, port);
                var done = await Task.WhenAny(connect, Task.Delay(300));
                return done == connect && client.Connected;
            }
            catch { return false; }
        }

        private void AppendLog(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_lock)
            {
                _log.AppendLine(line);
                if (_log.Length > 16_000)
                    _log.Remove(0, _log.Length - 8_000);
            }
        }

        private static string TrimLog(string log)
        {
            log = log.Trim();
            return log.Length > 600 ? log[^600..] : log;
        }

        public void Dispose() => Stop();
    }
}
