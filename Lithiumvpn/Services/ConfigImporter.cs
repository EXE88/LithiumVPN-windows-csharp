using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Lithiumvpn.Services.Xray;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// Turns arbitrary clipboard text into importable <see cref="LocalConfig"/>s,
    /// v2rayN-style. It accepts:
    ///  • a single share link (vless/vmess/trojan/ss/…),
    ///  • several links separated by new lines,
    ///  • a base64 "subscription blob" (whole clipboard is base64 of newline-joined links),
    ///  • a subscription URL — fetched directly (never through the tunnel), then decoded.
    /// Every candidate line is validated with <see cref="XrayLinkParser"/>; invalid
    /// lines are skipped and counted.
    /// </summary>
    public static class ConfigImporter
    {
        public sealed class ImportResult
        {
            public List<LocalConfig> Configs { get; } = new();
            public int Added => Configs.Count;
            public int Failed { get; set; }
            public bool AnyAdded => Configs.Count > 0;
        }

        // Direct (never proxied) client for fetching subscription URLs.
        private static readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        /// <summary>
        /// Parses clipboard text into configs, fetching a subscription URL when the
        /// text is a single http(s) link that resolves to a config list.
        /// </summary>
        public static async Task<ImportResult> ImportFromTextAsync(string? clipboardText)
        {
            var result = new ImportResult();
            if (string.IsNullOrWhiteSpace(clipboardText)) return result;

            var text = clipboardText.Trim();
            var lines = SplitLines(text);

            // A lone http(s) URL is very likely a subscription link — try to fetch it.
            // If the fetch yields nothing usable we fall back to treating the text as
            // a direct config (an http:// forward-proxy link is still valid).
            if (lines.Count == 1 && IsHttpUrl(lines[0]))
            {
                try
                {
                    var body = await _http.GetStringAsync(lines[0]);
                    ParseBlockInto(body, result);
                    if (result.AnyAdded) return result;
                }
                catch { /* fall through to direct parsing */ }
            }

            ParseBlockInto(text, result);
            return result;
        }

        /// <summary>Parses an already-fetched block of text (subscription body or pasted links).</summary>
        private static void ParseBlockInto(string block, ImportResult result)
        {
            if (string.IsNullOrWhiteSpace(block)) return;
            block = block.Trim();

            // Subscription bodies (and some v2rayN pastes) are base64 of the link list.
            if (!block.Contains("://") && TryDecodeBase64(block, out var decoded))
                block = decoded;

            foreach (var line in SplitLines(block))
            {
                if (TryBuildConfig(line, out var cfg))
                    result.Configs.Add(cfg);
                else
                    result.Failed++;
            }
        }

        /// <summary>Validates one line and builds a config, taking its name from the link's #fragment.</summary>
        private static bool TryBuildConfig(string line, out LocalConfig cfg)
        {
            cfg = new LocalConfig();
            line = line.Trim();
            if (line.Length == 0) return false;

            try
            {
                var parsed = XrayLinkParser.Parse(line);
                cfg.Link = line;
                cfg.Name = string.IsNullOrWhiteSpace(parsed.Remark) ? "" : parsed.Remark.Trim();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<string> SplitLines(string text)
        {
            var result = new List<string>();
            foreach (var raw in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length > 0) result.Add(line);
            }
            return result;
        }

        private static bool IsHttpUrl(string s) =>
            s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        private static bool TryDecodeBase64(string s, out string decoded)
        {
            decoded = "";
            try
            {
                s = s.Trim().Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4)
                {
                    case 2: s += "=="; break;
                    case 3: s += "="; break;
                    case 1: return false;
                }
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(s));
                return decoded.Contains("://");
            }
            catch
            {
                return false;
            }
        }
    }
}
