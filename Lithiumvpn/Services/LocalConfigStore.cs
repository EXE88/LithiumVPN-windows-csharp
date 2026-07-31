using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// One user-imported ("personal / local") VPN config.
    ///
    /// These are pasted from the clipboard (a single share link, several links, or a
    /// subscription) and are owned entirely by the user — the backend knows nothing
    /// about them, so they keep working while offline and when the user never bought
    /// a plan. <see cref="Link"/> is the raw share link fed straight to the Xray
    /// parser (same role the backend's <c>config_code</c> plays).
    /// </summary>
    public sealed class LocalConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Link { get; set; } = "";
    }

    /// <summary>
    /// Persists the user's imported configs to
    /// <c>%LOCALAPPDATA%\Lithiumvpn\local_configs.json</c> — the same plain-file
    /// approach used by <see cref="TokenStore"/> and the localization setting, since
    /// the app is unpackaged.
    /// </summary>
    public sealed class LocalConfigStore
    {
        private static readonly Lazy<LocalConfigStore> _lazy = new(() => new LocalConfigStore());
        public static LocalConfigStore Instance => _lazy.Value;

        private readonly List<LocalConfig> _configs = new();

        private LocalConfigStore() => Load();

        public IReadOnlyList<LocalConfig> Configs => _configs;
        public bool HasAny => _configs.Count > 0;

        /// <summary>Raised whenever the imported list changes so bound pages can re-render.</summary>
        public event Action? Changed;

        public LocalConfig Add(string link, string? name)
        {
            var cfg = new LocalConfig
            {
                Link = link.Trim(),
                Name = string.IsNullOrWhiteSpace(name) ? DefaultName() : name!.Trim()
            };
            _configs.Add(cfg);
            Save();
            Changed?.Invoke();
            return cfg;
        }

        /// <summary>Adds a batch (import) and raises <see cref="Changed"/> once.</summary>
        public void AddRange(IEnumerable<LocalConfig> items)
        {
            bool any = false;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Link)) continue;
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = DefaultName();
                _configs.Add(item);
                any = true;
            }
            if (!any) return;
            Save();
            Changed?.Invoke();
        }

        public void Remove(string id)
        {
            if (_configs.RemoveAll(c => c.Id == id) > 0)
            {
                Save();
                Changed?.Invoke();
            }
        }

        public void Rename(string id, string name)
        {
            var cfg = _configs.FirstOrDefault(c => c.Id == id);
            if (cfg is null) return;
            cfg.Name = string.IsNullOrWhiteSpace(name) ? DefaultName() : name.Trim();
            Save();
            Changed?.Invoke();
        }

        private string DefaultName() => $"Config {_configs.Count + 1}";

        // ─── Persistence ─────────────────────────────────────────────
        private static string FilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Lithiumvpn");
                return Path.Combine(dir, "local_configs.json");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<List<LocalConfig>>(json);
                if (data is not null)
                    _configs.AddRange(data.Where(c => !string.IsNullOrWhiteSpace(c.Link)));
            }
            catch { /* ignore a corrupt store */ }
        }

        private void Save()
        {
            try
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(_configs));
            }
            catch { /* ignore */ }
        }
    }
}
