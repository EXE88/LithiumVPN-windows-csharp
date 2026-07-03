using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lithiumvpn.Services.Xray
{
    /// <summary>
    /// Builds the full Xray client configuration around a parsed outbound:
    /// local SOCKS + HTTP inbounds on 127.0.0.1, the proxy outbound, and routing
    /// that keeps private/LAN traffic direct so the local backend stays reachable.
    /// </summary>
    public static class XrayConfigBuilder
    {
        /// <param name="metricsPort">
        /// When set, enables the traffic-stats pipeline: the core counts per-outbound
        /// uplink/downlink bytes and exposes them over HTTP at
        /// <c>http://127.0.0.1:{metricsPort}/debug/vars</c>.
        /// </param>
        public static string Build(JsonObject proxyOutbound, int socksPort, int httpPort, int? metricsPort = null)
        {
            var config = new JsonObject
            {
                ["log"] = new JsonObject { ["loglevel"] = "warning" },

                ["inbounds"] = new JsonArray(
                    new JsonObject
                    {
                        ["tag"] = "socks-in",
                        ["listen"] = "127.0.0.1",
                        ["port"] = socksPort,
                        ["protocol"] = "socks",
                        ["settings"] = new JsonObject
                        {
                            ["auth"] = "noauth",
                            ["udp"] = true
                        },
                        ["sniffing"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["destOverride"] = new JsonArray("http", "tls", "quic")
                        }
                    },
                    new JsonObject
                    {
                        ["tag"] = "http-in",
                        ["listen"] = "127.0.0.1",
                        ["port"] = httpPort,
                        ["protocol"] = "http",
                        ["sniffing"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["destOverride"] = new JsonArray("http", "tls")
                        }
                    }),

                ["outbounds"] = new JsonArray(
                    proxyOutbound,
                    new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom" },
                    new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole" }),

                ["routing"] = new JsonObject
                {
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = new JsonArray(
                        // LAN / loopback stays direct (keeps the local backend reachable
                        // even while the system proxy routes browsers through the tunnel).
                        new JsonObject
                        {
                            ["type"] = "field",
                            ["ip"] = new JsonArray("geoip:private"),
                            ["outboundTag"] = "direct"
                        },
                        new JsonObject
                        {
                            ["type"] = "field",
                            ["domain"] = new JsonArray("localhost"),
                            ["outboundTag"] = "direct"
                        })
                }
            };

            if (metricsPort is int mp)
            {
                config["stats"] = new JsonObject();
                config["policy"] = new JsonObject
                {
                    ["system"] = new JsonObject
                    {
                        ["statsOutboundUplink"] = true,
                        ["statsOutboundDownlink"] = true
                    }
                };
                config["metrics"] = new JsonObject
                {
                    ["tag"] = "metrics",
                    ["listen"] = $"127.0.0.1:{mp}"
                };
            }

            return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
