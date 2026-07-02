using System;
using System.IO;
using System.Text.Json;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// Persists the JWT access / refresh token pair between sessions.
    ///
    /// The app is unpackaged, so — like the localization setting — this is stored
    /// as a plain file under <c>%LOCALAPPDATA%\Lithiumvpn</c>.
    /// </summary>
    public sealed class TokenStore
    {
        private static readonly Lazy<TokenStore> _lazy = new(() => new TokenStore());
        public static TokenStore Instance => _lazy.Value;

        private sealed class Persisted
        {
            public string? Access { get; set; }
            public string? Refresh { get; set; }
        }

        public string? AccessToken { get; private set; }
        public string? RefreshToken { get; private set; }

        public bool HasSession => !string.IsNullOrWhiteSpace(RefreshToken);

        private TokenStore()
        {
            Load();
        }

        public void SetTokens(string? access, string? refresh)
        {
            if (!string.IsNullOrWhiteSpace(access)) AccessToken = access;
            if (!string.IsNullOrWhiteSpace(refresh)) RefreshToken = refresh;
            Save();
        }

        public void UpdateAccessToken(string? access)
        {
            if (!string.IsNullOrWhiteSpace(access))
            {
                AccessToken = access;
                Save();
            }
        }

        public void Clear()
        {
            AccessToken = null;
            RefreshToken = null;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch { /* ignore */ }
        }

        private static string FilePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Lithiumvpn");
                return Path.Combine(dir, "session.json");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var json = File.ReadAllText(FilePath);
                var data = JsonSerializer.Deserialize<Persisted>(json);
                if (data is not null)
                {
                    AccessToken = data.Access;
                    RefreshToken = data.Refresh;
                }
            }
            catch { /* ignore corrupt session file */ }
        }

        private void Save()
        {
            try
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(new Persisted
                {
                    Access = AccessToken,
                    Refresh = RefreshToken
                });
                File.WriteAllText(path, json);
            }
            catch { /* ignore */ }
        }
    }
}
