using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lithiumvpn.Services.Xray
{
    /// <summary>
    /// User-defined system-proxy exceptions (domains / addresses that must NOT be
    /// tunneled). Persisted as JSON under <c>%LOCALAPPDATA%\Lithiumvpn</c> and
    /// merged into the WinINET bypass list every time the proxy is enabled.
    /// </summary>
    public static class ProxyBypassStore
    {
        private static readonly object _lock = new();
        private static List<string>? _entries;

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lithiumvpn", "proxy-bypass.json");

        public static IReadOnlyList<string> Entries
        {
            get { lock (_lock) return Load().ToList(); }
        }

        public static bool Add(string entry)
        {
            entry = Normalize(entry);
            if (entry.Length == 0) return false;
            lock (_lock)
            {
                var list = Load();
                if (list.Contains(entry, StringComparer.OrdinalIgnoreCase)) return false;
                list.Add(entry);
                Save(list);
                return true;
            }
        }

        public static bool Update(string oldEntry, string newEntry)
        {
            newEntry = Normalize(newEntry);
            if (newEntry.Length == 0) return false;
            lock (_lock)
            {
                var list = Load();
                int idx = list.FindIndex(e => string.Equals(e, oldEntry, StringComparison.OrdinalIgnoreCase));
                if (idx < 0) return false;
                // reject if the new value collides with a different existing entry
                if (list.Where((_, i) => i != idx)
                        .Contains(newEntry, StringComparer.OrdinalIgnoreCase)) return false;
                list[idx] = newEntry;
                Save(list);
                return true;
            }
        }

        public static void Remove(string entry)
        {
            lock (_lock)
            {
                var list = Load();
                if (list.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase)) > 0)
                    Save(list);
            }
        }

        /// <summary>Strips scheme/path noise so entries match what WinINET expects.</summary>
        private static string Normalize(string entry)
        {
            entry = (entry ?? "").Trim().TrimEnd(';');
            foreach (var prefix in new[] { "http://", "https://" })
                if (entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    entry = entry[prefix.Length..];
            var slash = entry.IndexOf('/');
            if (slash >= 0) entry = entry[..slash];
            return entry;
        }

        private static List<string> Load()
        {
            if (_entries is not null) return _entries;
            try
            {
                _entries = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new()
                    : new();
            }
            catch { _entries = new(); }
            return _entries;
        }

        private static void Save(List<string> list)
        {
            _entries = list;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(list,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* persisting is best-effort */ }
        }
    }
}
