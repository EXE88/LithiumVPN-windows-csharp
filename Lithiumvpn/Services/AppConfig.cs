using System;
using System.Collections.Generic;
using System.IO;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// Lightweight ".env" style configuration loader.
    ///
    /// The <c>.env</c> file is copied next to the executable at build time
    /// (see the csproj &lt;Content&gt; entry). Edit that file — or the copy in the
    /// output folder — to point the app at your backend without recompiling.
    ///
    /// A user override is also supported at
    /// <c>%LOCALAPPDATA%\Lithiumvpn\.env</c>, which takes priority so an installed
    /// app can be reconfigured without touching program files.
    /// </summary>
    public static class AppConfig
    {
        private static readonly Dictionary<string, string> _values =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;

        // ─── Defaults (used only if the key is absent everywhere) ───
        private const string DefaultBaseUrl = "http://127.0.0.1:8000";
        private const int DefaultTimeoutSeconds = 30;
        private const int DefaultHeartbeatSeconds = 20;
        private const string DefaultPingUrl = "https://www.google.com/generate_204";
        private const string DefaultTunnelCheckUrl = "http://cp.cloudflare.com/generate_204";

        /// <summary>Base URL of the API backend, without a trailing slash.</summary>
        public static string ApiBaseUrl
        {
            get
            {
                EnsureLoaded();
                var raw = Get("API_BASE_URL", DefaultBaseUrl);
                return string.IsNullOrWhiteSpace(raw)
                    ? DefaultBaseUrl
                    : raw.Trim().TrimEnd('/');
            }
        }

        /// <summary>Per-request network timeout.</summary>
        public static TimeSpan Timeout
        {
            get
            {
                EnsureLoaded();
                var raw = Get("API_TIMEOUT_SECONDS", DefaultTimeoutSeconds.ToString());
                return int.TryParse(raw, out var secs) && secs > 0
                    ? TimeSpan.FromSeconds(secs)
                    : TimeSpan.FromSeconds(DefaultTimeoutSeconds);
            }
        }

        /// <summary>
        /// How often the app polls the backend for fresh data (status, events,
        /// tickets) and reports the client online. Clamped to ≥ 5 seconds.
        /// </summary>
        public static TimeSpan HeartbeatInterval
        {
            get
            {
                EnsureLoaded();
                var raw = Get("HEARTBEAT_SECONDS", DefaultHeartbeatSeconds.ToString());
                return int.TryParse(raw, out var secs) && secs >= 5
                    ? TimeSpan.FromSeconds(secs)
                    : TimeSpan.FromSeconds(DefaultHeartbeatSeconds);
            }
        }

        /// <summary>URL used by the per-config ping probe (expects a 204/2xx response).</summary>
        public static string PingUrl
        {
            get
            {
                EnsureLoaded();
                var raw = Get("PING_URL", DefaultPingUrl).Trim();
                return string.IsNullOrWhiteSpace(raw) ? DefaultPingUrl : raw;
            }
        }

        /// <summary>
        /// URL used to verify the tunnel passes traffic right after connecting and
        /// for the dashboard's live latency while connected.
        /// </summary>
        public static string TunnelCheckUrl
        {
            get
            {
                EnsureLoaded();
                var raw = Get("TUNNEL_CHECK_URL", DefaultTunnelCheckUrl).Trim();
                return string.IsNullOrWhiteSpace(raw) ? DefaultTunnelCheckUrl : raw;
            }
        }

        public static string Get(string key, string fallback = "")
        {
            EnsureLoaded();
            return _values.TryGetValue(key, out var v) ? v : fallback;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            // Load the packaged .env first (lowest priority)…
            LoadFile(Path.Combine(AppContext.BaseDirectory, ".env"));

            // …then the per-user override, which wins for any duplicate keys.
            try
            {
                var userEnv = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Lithiumvpn", ".env");
                LoadFile(userEnv);
            }
            catch { /* ignore */ }
        }

        private static void LoadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line[..eq].Trim();
                    var value = line[(eq + 1)..].Trim();

                    // Strip optional surrounding quotes.
                    if (value.Length >= 2 &&
                        ((value[0] == '"' && value[^1] == '"') ||
                         (value[0] == '\'' && value[^1] == '\'')))
                    {
                        value = value[1..^1];
                    }

                    _values[key] = value;
                }
            }
            catch { /* a malformed .env should never crash startup */ }
        }
    }
}
