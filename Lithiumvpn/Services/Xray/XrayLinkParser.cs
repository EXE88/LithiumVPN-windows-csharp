using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lithiumvpn.Services.Xray
{
    /// <summary>
    /// Parses any share link / config text into a valid Xray outbound object.
    ///
    /// Supported inputs:
    ///  • vless://uuid@host:port?params#name         (standard share-link, incl. REALITY / XTLS vision)
    ///  • vmess://base64(json)                        (v2rayN format) and vmess://uuid@host:port?params
    ///  • trojan://password@host:port?params#name
    ///  • ss://…                                      (SIP002, legacy full-base64, and plain userinfo)
    ///  • socks:// and http(s):// forward proxies
    ///  • raw JSON — either a bare outbound object, or a full client config
    ///    (the first non-direct/blackhole outbound is extracted)
    ///
    /// Supported transports: tcp, raw, kcp/mkcp, ws/websocket, http/h2,
    /// grpc/gun, httpupgrade, xhttp/splithttp, quic.
    /// Supported securities: none, tls, reality.
    /// Unknown query parameters are ignored; recognised ones are mapped even when
    /// links carry any number of parameters in any order.
    /// </summary>
    public static class XrayLinkParser
    {
        /// <summary>Parsed result: the outbound node plus the remote endpoint (for TCP ping).</summary>
        public sealed class ParsedConfig
        {
            public JsonObject Outbound { get; init; } = new();
            public string Address { get; init; } = "";
            public int Port { get; init; }
            public string Protocol { get; init; } = "";
            public string Remark { get; init; } = "";
        }

        public static ParsedConfig Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new FormatException("Config is empty.");

            input = input.Trim();

            // Raw JSON (bare outbound or full config)
            if (input.StartsWith("{"))
                return ParseJson(input);

            var scheme = input.Split("://", 2)[0].ToLowerInvariant();
            return scheme switch
            {
                "vless" => ParseVless(input),
                "vmess" => ParseVmess(input),
                "trojan" => ParseTrojan(input),
                "ss" => ParseShadowsocks(input),
                "socks" or "socks5" => ParseSocksOrHttp(input, "socks"),
                "http" or "https" => ParseSocksOrHttp(input, "http"),
                _ => throw new FormatException($"Unsupported config scheme '{scheme}'.")
            };
        }

        /// <summary>Extracts just host/port without building a full outbound (used for ping).</summary>
        public static bool TryGetEndpoint(string input, out string host, out int port)
        {
            host = ""; port = 0;
            try
            {
                var parsed = Parse(input);
                host = parsed.Address;
                port = parsed.Port;
                return !string.IsNullOrEmpty(host) && port > 0;
            }
            catch { return false; }
        }

        // ─────────────────────────────────────────────────────────
        //  VLESS
        // ─────────────────────────────────────────────────────────
        private static ParsedConfig ParseVless(string link)
        {
            var u = ParseUri(link);
            var q = u.Query;

            var user = new JsonObject
            {
                ["id"] = u.UserInfo,
                ["encryption"] = Get(q, "encryption", "none"),
            };
            var flow = Get(q, "flow");
            if (!string.IsNullOrEmpty(flow)) user["flow"] = flow;

            var outbound = new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "vless",
                ["settings"] = new JsonObject
                {
                    ["vnext"] = new JsonArray(new JsonObject
                    {
                        ["address"] = u.Host,
                        ["port"] = u.Port,
                        ["users"] = new JsonArray(user)
                    })
                },
                ["streamSettings"] = BuildStreamSettings(q, u.Host)
            };

            return Result(outbound, u, "vless");
        }

        // ─────────────────────────────────────────────────────────
        //  VMess (v2rayN base64-JSON, with query-param fallback)
        // ─────────────────────────────────────────────────────────
        private static ParsedConfig ParseVmess(string link)
        {
            var payload = link["vmess://".Length..];

            // Standard style vmess://uuid@host:port?params (rare but valid)
            if (payload.Contains('@') && !payload.Contains('{'))
            {
                var u = ParseUri(link);
                var outboundStd = BuildVmessOutbound(
                    u.Host, u.Port, u.UserInfo,
                    Get(u.Query, "alterId", "0"),
                    Get(u.Query, "scy", Get(u.Query, "encryption", "auto")),
                    BuildStreamSettings(u.Query, u.Host));
                return Result(outboundStd, u, "vmess");
            }

            // v2rayN: whole payload is base64 JSON
            string json;
            try { json = Encoding.UTF8.GetString(DecodeBase64(StripFragment(payload, out _))); }
            catch { throw new FormatException("Invalid vmess link (not valid base64)."); }

            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            string S(string name, string fallback = "") =>
                r.TryGetProperty(name, out var v)
                    ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? fallback : v.ToString())
                    : fallback;

            string host = S("add");
            int port = int.TryParse(S("port"), out var p) ? p : 0;
            string net = S("net", "tcp");

            // Map the vmess-JSON fields onto the same query dictionary the
            // stream-settings builder consumes, so one code path handles all links.
            var q = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["type"] = net,
                ["security"] = S("tls") is "tls" or "reality" ? S("tls") : (S("tls") == "1" ? "tls" : "none"),
                ["host"] = S("host"),
                ["path"] = S("path"),
                ["sni"] = S("sni"),
                ["alpn"] = S("alpn"),
                ["fp"] = S("fp"),
            };
            // In vmess JSON, "type" holds the header type (e.g. http obfuscation for tcp/kcp).
            var headerType = S("type");
            if (!string.IsNullOrEmpty(headerType) && headerType != "none")
                q["headerType"] = headerType;
            if (net is "grpc")
            {
                q["serviceName"] = S("path");
                q["mode"] = string.IsNullOrEmpty(S("mode")) ? "gun" : S("mode");
            }
            if (net is "quic")
            {
                q["quicSecurity"] = S("host", "none");
                q["key"] = S("path");
            }
            if (net is "kcp" or "mkcp")
                q["seed"] = S("path");

            var outbound = BuildVmessOutbound(host, port, S("id"), S("aid", "0"), S("scy", "auto"),
                BuildStreamSettings(q, host));

            return new ParsedConfig
            {
                Outbound = outbound,
                Address = host,
                Port = port,
                Protocol = "vmess",
                Remark = S("ps")
            };
        }

        private static JsonObject BuildVmessOutbound(
            string host, int port, string id, string alterId, string security, JsonObject stream) => new()
        {
            ["tag"] = "proxy",
            ["protocol"] = "vmess",
            ["settings"] = new JsonObject
            {
                ["vnext"] = new JsonArray(new JsonObject
                {
                    ["address"] = host,
                    ["port"] = port,
                    ["users"] = new JsonArray(new JsonObject
                    {
                        ["id"] = id,
                        ["alterId"] = int.TryParse(alterId, out var aid) ? aid : 0,
                        ["security"] = string.IsNullOrEmpty(security) ? "auto" : security
                    })
                })
            },
            ["streamSettings"] = stream
        };

        // ─────────────────────────────────────────────────────────
        //  Trojan
        // ─────────────────────────────────────────────────────────
        private static ParsedConfig ParseTrojan(string link)
        {
            var u = ParseUri(link);
            var q = u.Query;

            // Trojan defaults to TLS when the link omits `security`.
            if (!q.ContainsKey("security")) q["security"] = "tls";

            var server = new JsonObject
            {
                ["address"] = u.Host,
                ["port"] = u.Port,
                ["password"] = u.UserInfo
            };
            var flow = Get(q, "flow");
            if (!string.IsNullOrEmpty(flow)) server["flow"] = flow;

            var outbound = new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "trojan",
                ["settings"] = new JsonObject { ["servers"] = new JsonArray(server) },
                ["streamSettings"] = BuildStreamSettings(q, u.Host)
            };

            return Result(outbound, u, "trojan");
        }

        // ─────────────────────────────────────────────────────────
        //  Shadowsocks — SIP002 / legacy / plain
        // ─────────────────────────────────────────────────────────
        private static ParsedConfig ParseShadowsocks(string link)
        {
            var body = StripFragment(link["ss://".Length..], out var remark);

            string method, password, host;
            int port;

            if (body.Contains('@'))
            {
                var at = body.LastIndexOf('@');
                var userInfo = Uri.UnescapeDataString(body[..at]);
                var hostPart = body[(at + 1)..];

                // SIP002: userinfo may be base64("method:password") or plain "method:password"
                if (!userInfo.Contains(':'))
                {
                    try { userInfo = Encoding.UTF8.GetString(DecodeBase64(userInfo)); }
                    catch { throw new FormatException("Invalid ss link userinfo."); }
                }
                var ci = userInfo.IndexOf(':');
                if (ci < 0) throw new FormatException("Invalid ss link (missing method:password).");
                method = userInfo[..ci];
                password = userInfo[(ci + 1)..];

                // strip ?plugin=… — plugins are not supported by a bare xray core
                var qm = hostPart.IndexOf('?');
                if (qm >= 0) hostPart = hostPart[..qm];
                (host, port) = SplitHostPort(hostPart);
            }
            else
            {
                // Legacy: everything base64("method:password@host:port")
                string decoded;
                try { decoded = Encoding.UTF8.GetString(DecodeBase64(body)); }
                catch { throw new FormatException("Invalid ss link (not valid base64)."); }

                var at = decoded.LastIndexOf('@');
                if (at < 0) throw new FormatException("Invalid ss link.");
                var ci = decoded.IndexOf(':');
                method = decoded[..ci];
                password = decoded[(ci + 1)..at];
                (host, port) = SplitHostPort(decoded[(at + 1)..]);
            }

            var outbound = new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = "shadowsocks",
                ["settings"] = new JsonObject
                {
                    ["servers"] = new JsonArray(new JsonObject
                    {
                        ["address"] = host,
                        ["port"] = port,
                        ["method"] = method,
                        ["password"] = password,
                        ["uot"] = true
                    })
                },
                ["streamSettings"] = new JsonObject { ["network"] = "tcp" }
            };

            return new ParsedConfig
            {
                Outbound = outbound,
                Address = host,
                Port = port,
                Protocol = "shadowsocks",
                Remark = remark
            };
        }

        // ─────────────────────────────────────────────────────────
        //  SOCKS / HTTP forward proxies
        // ─────────────────────────────────────────────────────────
        private static ParsedConfig ParseSocksOrHttp(string link, string protocol)
        {
            var u = ParseUri(link);

            var server = new JsonObject { ["address"] = u.Host, ["port"] = u.Port };

            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                var info = u.UserInfo;
                // socks links often base64-encode "user:pass"
                if (!info.Contains(':'))
                {
                    try { info = Encoding.UTF8.GetString(DecodeBase64(info)); }
                    catch { /* keep as-is */ }
                }
                var ci = info.IndexOf(':');
                if (ci > 0)
                {
                    server["users"] = new JsonArray(new JsonObject
                    {
                        ["user"] = info[..ci],
                        ["pass"] = info[(ci + 1)..]
                    });
                }
            }

            var stream = new JsonObject { ["network"] = "tcp" };
            if (link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                Get(u.Query, "security") == "tls")
            {
                stream["security"] = "tls";
                stream["tlsSettings"] = new JsonObject
                {
                    ["serverName"] = Get(u.Query, "sni", u.Host)
                };
            }

            var outbound = new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = protocol,
                ["settings"] = new JsonObject { ["servers"] = new JsonArray(server) },
                ["streamSettings"] = stream
            };

            return Result(outbound, u, protocol);
        }

        // ─────────────────────────────────────────────────────────
        //  Raw JSON input
        // ─────────────────────────────────────────────────────────
        private static ParsedConfig ParseJson(string json)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(json); }
            catch (Exception ex) { throw new FormatException($"Invalid JSON config: {ex.Message}"); }

            if (node is not JsonObject obj)
                throw new FormatException("JSON config must be an object.");

            JsonObject outbound;
            if (obj["outbounds"] is JsonArray outbounds)
            {
                // Full client config: take the first real proxy outbound.
                outbound = outbounds.OfType<JsonObject>()
                    .FirstOrDefault(o =>
                    {
                        var proto = (string?)o["protocol"] ?? "";
                        return proto is not ("freedom" or "blackhole" or "dns");
                    })
                    ?? throw new FormatException("JSON config contains no proxy outbound.");
                outbound = (JsonObject)outbound.DeepClone();
            }
            else if (obj["protocol"] is not null)
            {
                outbound = (JsonObject)obj.DeepClone();
            }
            else
            {
                throw new FormatException("JSON config must be an outbound object or contain \"outbounds\".");
            }

            outbound["tag"] = "proxy";
            var (host, port) = ExtractEndpoint(outbound);
            return new ParsedConfig
            {
                Outbound = outbound,
                Address = host,
                Port = port,
                Protocol = (string?)outbound["protocol"] ?? "",
                Remark = ""
            };
        }

        private static (string, int) ExtractEndpoint(JsonObject outbound)
        {
            var settings = outbound["settings"] as JsonObject;
            var list = (settings?["vnext"] ?? settings?["servers"]) as JsonArray;
            if (list?.FirstOrDefault() is JsonObject first)
                return ((string?)first["address"] ?? "", (int?)first["port"] ?? 0);
            return ("", 0);
        }

        // ─────────────────────────────────────────────────────────
        //  streamSettings from query parameters (shared by all schemes)
        // ─────────────────────────────────────────────────────────
        private static JsonObject BuildStreamSettings(Dictionary<string, string> q, string fallbackSni)
        {
            string network = Get(q, "type", "tcp").ToLowerInvariant();
            // normalize aliases
            network = network switch
            {
                "websocket" => "ws",
                "mkcp" => "kcp",
                "h2" or "http2" => "http",
                "gun" => "grpc",
                "splithttp" => "xhttp",
                "raw" => "tcp",
                _ => network
            };

            var stream = new JsonObject { ["network"] = network };

            // ── transport-specific settings ──
            switch (network)
            {
                case "ws":
                {
                    var ws = new JsonObject { ["path"] = Get(q, "path", "/") };
                    var host = Get(q, "host");
                    if (!string.IsNullOrEmpty(host)) ws["host"] = host;
                    stream["wsSettings"] = ws;
                    break;
                }
                case "grpc":
                {
                    var grpc = new JsonObject
                    {
                        ["serviceName"] = Get(q, "serviceName", ""),
                        ["multiMode"] = Get(q, "mode") == "multi"
                    };
                    var authority = Get(q, "authority");
                    if (!string.IsNullOrEmpty(authority)) grpc["authority"] = authority;
                    stream["grpcSettings"] = grpc;
                    break;
                }
                case "http":
                {
                    // The plain HTTP/2 transport was removed from current Xray cores;
                    // its sanctioned replacement is XHTTP in stream-one mode.
                    stream["network"] = "xhttp";
                    var h2 = new JsonObject
                    {
                        ["path"] = Get(q, "path", "/"),
                        ["mode"] = "stream-one"
                    };
                    var h2host = Get(q, "host").Split(',')[0].Trim();
                    if (!string.IsNullOrEmpty(h2host)) h2["host"] = h2host;
                    stream["xhttpSettings"] = h2;
                    break;
                }
                case "httpupgrade":
                {
                    var hu = new JsonObject { ["path"] = Get(q, "path", "/") };
                    var host = Get(q, "host");
                    if (!string.IsNullOrEmpty(host)) hu["host"] = host;
                    stream["httpupgradeSettings"] = hu;
                    break;
                }
                case "xhttp":
                {
                    var xh = new JsonObject { ["path"] = Get(q, "path", "/") };
                    var host = Get(q, "host");
                    if (!string.IsNullOrEmpty(host)) xh["host"] = host;
                    var mode = Get(q, "mode");
                    if (!string.IsNullOrEmpty(mode) && mode != "auto") xh["mode"] = mode;
                    var extra = Get(q, "extra");
                    if (!string.IsNullOrEmpty(extra))
                    {
                        try { xh["extra"] = JsonNode.Parse(extra); }
                        catch { /* malformed extra — ignore */ }
                    }
                    stream["xhttpSettings"] = xh;
                    break;
                }
                case "kcp":
                {
                    // Current Xray cores replaced mKCP `header`/`seed` with a single
                    // `finalMask` value ("mkcp-aes128gcm:<seed>" / "header-<type>").
                    var kcp = new JsonObject();
                    var seed = Get(q, "seed");
                    var headerType = Get(q, "headerType", "none");
                    if (!string.IsNullOrEmpty(seed))
                        kcp["finalMask"] = $"mkcp-aes128gcm:{seed}";
                    else if (headerType != "none")
                        kcp["finalMask"] = $"header-{headerType}";
                    stream["kcpSettings"] = kcp;
                    break;
                }
                case "quic":
                    // Removed from current Xray cores (migrated to XHTTP stream-one H3).
                    throw new FormatException(
                        "QUIC transport is no longer supported by the Xray core; use an XHTTP (H3) config instead.");
                default: // tcp
                {
                    var headerType = Get(q, "headerType");
                    if (headerType == "http")
                    {
                        var request = new JsonObject
                        {
                            ["path"] = new JsonArray(Get(q, "path", "/").Split(',')
                                .Select(p => (JsonNode?)p.Trim()).ToArray())
                        };
                        var host = Get(q, "host");
                        if (!string.IsNullOrEmpty(host))
                            request["headers"] = new JsonObject
                            {
                                ["Host"] = new JsonArray(host.Split(',')
                                    .Select(h => (JsonNode?)h.Trim()).ToArray())
                            };
                        stream["tcpSettings"] = new JsonObject
                        {
                            ["header"] = new JsonObject
                            {
                                ["type"] = "http",
                                ["request"] = request
                            }
                        };
                    }
                    break;
                }
            }

            // ── security layer ──
            string security = Get(q, "security", "none").ToLowerInvariant();
            if (security is "tls" or "xtls")
            {
                stream["security"] = "tls";
                var tls = new JsonObject
                {
                    ["serverName"] = FirstNonEmpty(Get(q, "sni"), Get(q, "host"), fallbackSni)
                };
                var alpn = Get(q, "alpn");
                if (!string.IsNullOrEmpty(alpn))
                    tls["alpn"] = new JsonArray(Uri.UnescapeDataString(alpn).Split(',')
                        .Select(a => (JsonNode?)a.Trim()).ToArray());
                var fp = Get(q, "fp");
                if (!string.IsNullOrEmpty(fp)) tls["fingerprint"] = fp;
                // `allowInsecure` was removed from current Xray cores
                // (migrated to pinnedPeerCertSha256) — never emit it.
                stream["tlsSettings"] = tls;
            }
            else if (security == "reality")
            {
                stream["security"] = "reality";
                var reality = new JsonObject
                {
                    ["serverName"] = FirstNonEmpty(Get(q, "sni"), fallbackSni),
                    ["publicKey"] = Get(q, "pbk"),
                    ["shortId"] = Get(q, "sid", ""),
                    ["spiderX"] = Uri.UnescapeDataString(Get(q, "spx", "")),
                    ["fingerprint"] = FirstNonEmpty(Get(q, "fp"), "chrome")
                };
                stream["realitySettings"] = reality;
            }

            return stream;
        }

        // ─────────────────────────────────────────────────────────
        //  URI helpers (Uri class chokes on some share links, parse manually)
        // ─────────────────────────────────────────────────────────
        private sealed class LinkParts
        {
            public string UserInfo = "";
            public string Host = "";
            public int Port;
            public Dictionary<string, string> Query = new(StringComparer.OrdinalIgnoreCase);
            public string Fragment = "";
        }

        private static LinkParts ParseUri(string link)
        {
            var parts = new LinkParts();

            var idx = link.IndexOf("://", StringComparison.Ordinal);
            var rest = link[(idx + 3)..];

            rest = StripFragment(rest, out parts.Fragment);

            var qIdx = rest.IndexOf('?');
            if (qIdx >= 0)
            {
                foreach (var pair in rest[(qIdx + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    var key = Uri.UnescapeDataString(pair[..eq]);
                    var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                    parts.Query[key] = value;
                }
                rest = rest[..qIdx];
            }

            var at = rest.LastIndexOf('@');
            if (at >= 0)
            {
                parts.UserInfo = Uri.UnescapeDataString(rest[..at]);
                rest = rest[(at + 1)..];
            }

            (parts.Host, parts.Port) = SplitHostPort(rest.TrimEnd('/'));
            return parts;
        }

        private static (string Host, int Port) SplitHostPort(string s)
        {
            // IPv6 literal: [::1]:443
            if (s.StartsWith("["))
            {
                var close = s.IndexOf(']');
                if (close < 0) throw new FormatException("Invalid IPv6 host.");
                var host = s[1..close];
                var portPart = s[(close + 1)..].TrimStart(':');
                return (host, int.TryParse(portPart, out var p6) ? p6 : 0);
            }

            var ci = s.LastIndexOf(':');
            if (ci < 0) return (s, 0);
            return (s[..ci], int.TryParse(s[(ci + 1)..], out var p) ? p : 0);
        }

        private static string StripFragment(string s, out string fragment)
        {
            fragment = "";
            var h = s.IndexOf('#');
            if (h >= 0)
            {
                fragment = Uri.UnescapeDataString(s[(h + 1)..]);
                s = s[..h];
            }
            return s;
        }

        private static byte[] DecodeBase64(string s)
        {
            s = s.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        private static string Get(Dictionary<string, string> q, string key, string fallback = "") =>
            q.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : fallback;

        private static string FirstNonEmpty(params string[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

        private static ParsedConfig Result(JsonObject outbound, LinkParts u, string protocol) => new()
        {
            Outbound = outbound,
            Address = u.Host,
            Port = u.Port,
            Protocol = protocol,
            Remark = u.Fragment
        };
    }
}
