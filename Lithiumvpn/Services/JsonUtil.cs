using System;
using System.Globalization;
using System.Text.Json;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// Helpers for reading loosely-typed JSON objects (tickets, messages, plans)
    /// whose exact field names aren't pinned down by the schema. Each getter tries
    /// several candidate property names, case-insensitively.
    /// </summary>
    public static class JsonUtil
    {
        public static string? GetString(JsonElement el, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;
            foreach (var name in names)
            {
                if (TryFind(el, name, out var v))
                {
                    switch (v.ValueKind)
                    {
                        case JsonValueKind.String: return v.GetString();
                        case JsonValueKind.Number: return v.ToString();
                        case JsonValueKind.True: return "true";
                        case JsonValueKind.False: return "false";
                    }
                }
            }
            return null;
        }

        public static int GetInt(JsonElement el, int fallback, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return fallback;
            foreach (var name in names)
            {
                if (TryFind(el, name, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
                    if (v.ValueKind == JsonValueKind.String &&
                        int.TryParse(v.GetString(), out var si)) return si;
                }
            }
            return fallback;
        }

        public static bool GetBool(JsonElement el, bool fallback, params string[] names)
        {
            if (el.ValueKind != JsonValueKind.Object) return fallback;
            foreach (var name in names)
            {
                if (TryFind(el, name, out var v))
                {
                    switch (v.ValueKind)
                    {
                        case JsonValueKind.True: return true;
                        case JsonValueKind.False: return false;
                        case JsonValueKind.Number when v.TryGetInt32(out var n): return n != 0;
                        case JsonValueKind.String:
                            var s = v.GetString()?.Trim().ToLowerInvariant();
                            if (s is "true" or "1" or "yes") return true;
                            if (s is "false" or "0" or "no") return false;
                            break;
                    }
                }
            }
            return fallback;
        }

        /// <summary>Parses a date/time string into local time, returns null if unparseable.</summary>
        public static DateTime? GetDateTime(JsonElement el, params string[] names)
        {
            var s = GetString(el, names);
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
                return dt.ToLocalTime();
            return null;
        }

        private static bool TryFind(JsonElement obj, string name, out JsonElement value)
        {
            foreach (var prop in obj.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
            value = default;
            return false;
        }
    }
}
