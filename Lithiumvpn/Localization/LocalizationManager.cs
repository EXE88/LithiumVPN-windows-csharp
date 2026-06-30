using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;

namespace Lithiumvpn.Localization
{
    /// <summary>
    /// Runtime localization service for English / Persian.
    /// Bind in XAML with:  Text="{Binding [SomeKey], Source={StaticResource Loc}}"
    /// FlowDirection with:  FlowDirection="{Binding FlowDirection, Source={StaticResource Loc}}"
    /// Switching the language raises change notifications so every binding refreshes live.
    /// </summary>
    public sealed class LocalizationManager : INotifyPropertyChanged
    {
        public const string English = "en";
        public const string Persian = "fa";

        // The instance created by XAML (declared as a resource in App.xaml) becomes the shared
        // singleton, so XAML bindings and code-behind always talk to the same object.
        private static LocalizationManager? _instance;
        public static LocalizationManager Instance => _instance ??= new LocalizationManager();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Fired after the active language changes (for code-behind that sets text imperatively).</summary>
        public event Action? LanguageChanged;

        private string _language = English;
        private Dictionary<string, string> _current = Strings.En;

        // Public parameterless ctor so XAML (App.xaml) can create it as a {StaticResource}.
        // App.xaml resolves this resource during startup, before any code touches Instance,
        // so the XAML-created object is the one shared everywhere.
        public LocalizationManager()
        {
            _instance = this;

            var saved = LoadSavedLanguage();
            if (saved == Persian)
            {
                _language = Persian;
                _current = Strings.Fa;
            }
        }

        /// <summary>Indexer used by XAML bindings — returns the localized string for a key.</summary>
        public string this[string key]
        {
            get
            {
                if (key is null) return string.Empty;
                if (_current.TryGetValue(key, out var value)) return value;
                // Fallback to English so a missing translation never shows an empty label.
                if (Strings.En.TryGetValue(key, out var en)) return en;
                return key;
            }
        }

        /// <summary>Convenience accessor for code-behind.</summary>
        public string Get(string key) => this[key];

        public string CurrentLanguage => _language;

        public bool IsRtl => _language == Persian;

        public FlowDirection FlowDirection => IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public void ChangeLanguage(string language)
        {
            if (language != English && language != Persian) return;
            if (language == _language) return;

            _language = language;
            _current = language == Persian ? Strings.Fa : Strings.En;
            SaveLanguage(language);

            // Refresh every indexer binding + the direction-related properties.
            Raise(string.Empty);          // refresh all
            Raise("Item[]");              // refresh indexer bindings explicitly
            Raise(nameof(FlowDirection));
            Raise(nameof(IsRtl));
            Raise(nameof(CurrentLanguage));

            LanguageChanged?.Invoke();
        }

        private void Raise(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        // ─── Persistence (app is unpackaged, so use a plain file in LocalAppData) ───
        private static string SettingsPath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Lithiumvpn");
                return Path.Combine(dir, "language.txt");
            }
        }

        private static string LoadSavedLanguage()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return File.ReadAllText(SettingsPath).Trim();
            }
            catch { }
            return English;
        }

        private static void SaveLanguage(string language)
        {
            try
            {
                var path = SettingsPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, language);
            }
            catch { }
        }
    }
}
