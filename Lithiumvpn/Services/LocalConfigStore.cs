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

        /// <summary>
        /// Empty for a manually pasted config; otherwise the id of the subscription
        /// it came from. Configs never move between groups — updating a subscription
        /// replaces only its own configs.
        /// </summary>
        public string SubscriptionId { get; set; } = "";
    }

    /// <summary>
    /// A subscription the user imported. Each one stays its own group: several
    /// subscriptions are never merged, and refreshing one replaces only its configs.
    /// </summary>
    public sealed class LocalSubscription
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";

        /// <summary>UTC ISO-8601 timestamp of the last successful refresh (null = never).</summary>
        public string? UpdatedAt { get; set; }
    }

    /// <summary>
    /// Persists the user's imported configs and subscriptions to
    /// <c>%LOCALAPPDATA%\Lithiumvpn\local_configs.json</c> — the same plain-file
    /// approach used by <see cref="TokenStore"/> and the localization setting, since
    /// the app is unpackaged.
    /// </summary>
    public sealed class LocalConfigStore
    {
        private static readonly Lazy<LocalConfigStore> _lazy = new(() => new LocalConfigStore());
        public static LocalConfigStore Instance => _lazy.Value;

        private readonly List<LocalConfig> _configs = new();
        private readonly List<LocalSubscription> _subscriptions = new();

        private LocalConfigStore() => Load();

        /// <summary>Every imported config, across all groups.</summary>
        public IReadOnlyList<LocalConfig> Configs => _configs;

        /// <summary>Configs pasted one by one (not owned by any subscription).</summary>
        public IReadOnlyList<LocalConfig> ManualConfigs =>
            _configs.Where(c => string.IsNullOrEmpty(c.SubscriptionId)).ToList();

        public IReadOnlyList<LocalSubscription> Subscriptions => _subscriptions;

        public IReadOnlyList<LocalConfig> ConfigsFor(string subscriptionId) =>
            _configs.Where(c => c.SubscriptionId == subscriptionId).ToList();

        public bool HasAny => _configs.Count > 0;

        /// <summary>Raised whenever the imported list changes so bound pages can re-render.</summary>
        public event Action? Changed;

        // ─── Manually pasted configs ─────────────────────────────────
        public void AddManual(IEnumerable<LocalConfig> items)
        {
            bool any = false;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Link)) continue;
                item.SubscriptionId = "";
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = DefaultConfigName();
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
            cfg.Name = string.IsNullOrWhiteSpace(name) ? DefaultConfigName() : name.Trim();
            Save();
            Changed?.Invoke();
        }

        // ─── Subscriptions ───────────────────────────────────────────

        /// <summary>Creates a new subscription group and stores its configs.</summary>
        public LocalSubscription AddSubscription(string url, string? name, IEnumerable<LocalConfig> items)
        {
            var sub = new LocalSubscription
            {
                Url = url.Trim(),
                Name = string.IsNullOrWhiteSpace(name) ? DefaultSubscriptionName(url) : name!.Trim(),
                UpdatedAt = DateTime.UtcNow.ToString("o"),
            };
            _subscriptions.Add(sub);

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Link)) continue;
                item.SubscriptionId = sub.Id;
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = DefaultConfigName();
                _configs.Add(item);
            }

            Save();
            Changed?.Invoke();
            return sub;
        }

        /// <summary>
        /// Swaps a subscription's configs for a freshly fetched set. Only that
        /// subscription's configs are touched — manual configs and other
        /// subscriptions are left exactly as they were.
        /// </summary>
        public void ReplaceSubscriptionConfigs(string subscriptionId, IEnumerable<LocalConfig> items)
        {
            var sub = _subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
            if (sub is null) return;

            _configs.RemoveAll(c => c.SubscriptionId == subscriptionId);
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Link)) continue;
                item.SubscriptionId = subscriptionId;
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = DefaultConfigName();
                _configs.Add(item);
            }

            sub.UpdatedAt = DateTime.UtcNow.ToString("o");
            Save();
            Changed?.Invoke();
        }

        /// <summary>Deletes a subscription along with every config it brought in.</summary>
        public void RemoveSubscription(string subscriptionId)
        {
            int removed = _subscriptions.RemoveAll(s => s.Id == subscriptionId);
            _configs.RemoveAll(c => c.SubscriptionId == subscriptionId);
            if (removed == 0) return;
            Save();
            Changed?.Invoke();
        }

        public void RenameSubscription(string subscriptionId, string name)
        {
            var sub = _subscriptions.FirstOrDefault(s => s.Id == subscriptionId);
            if (sub is null) return;
            sub.Name = string.IsNullOrWhiteSpace(name) ? DefaultSubscriptionName(sub.Url) : name.Trim();
            Save();
            Changed?.Invoke();
        }

        /// <summary>Finds an existing subscription with the same URL, if any.</summary>
        public LocalSubscription? FindSubscriptionByUrl(string url)
        {
            var trimmed = url.Trim();
            return _subscriptions.FirstOrDefault(
                s => string.Equals(s.Url, trimmed, StringComparison.OrdinalIgnoreCase));
        }

        // ─── Naming helpers ──────────────────────────────────────────
        private string DefaultConfigName() => $"Config {_configs.Count + 1}";

        private string DefaultSubscriptionName(string url)
        {
            // Prefer the host so several subscriptions stay tellable apart.
            try
            {
                var host = new Uri(url.Trim()).Host;
                if (!string.IsNullOrWhiteSpace(host)) return host;
            }
            catch { /* not a well-formed URL — fall through */ }
            return $"Subscription {_subscriptions.Count + 1}";
        }

        // ─── Persistence ─────────────────────────────────────────────
        private sealed class Persisted
        {
            public List<LocalConfig> Configs { get; set; } = new();
            public List<LocalSubscription> Subscriptions { get; set; } = new();
        }

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
                if (string.IsNullOrWhiteSpace(json)) return;

                // Current format is an object; the first release wrote a bare array,
                // so fall back to that so early imports aren't lost.
                if (json.TrimStart().StartsWith("["))
                {
                    var legacy = JsonSerializer.Deserialize<List<LocalConfig>>(json);
                    if (legacy is not null)
                        _configs.AddRange(legacy.Where(c => !string.IsNullOrWhiteSpace(c.Link)));
                    return;
                }

                var data = JsonSerializer.Deserialize<Persisted>(json);
                if (data is null) return;

                _subscriptions.AddRange(data.Subscriptions);
                _configs.AddRange(data.Configs.Where(c => !string.IsNullOrWhiteSpace(c.Link)));

                // Drop configs whose subscription vanished (hand-edited file).
                var ids = _subscriptions.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
                foreach (var cfg in _configs)
                    if (!string.IsNullOrEmpty(cfg.SubscriptionId) && !ids.Contains(cfg.SubscriptionId))
                        cfg.SubscriptionId = "";
            }
            catch { /* ignore a corrupt store */ }
        }

        private void Save()
        {
            try
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(new Persisted
                {
                    Configs = _configs,
                    Subscriptions = _subscriptions,
                }));
            }
            catch { /* ignore */ }
        }
    }
}
