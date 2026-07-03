using Microsoft.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Lithiumvpn.Services.Xray
{
    /// <summary>
    /// Sets / clears the Windows (WinINET) system proxy so all system traffic
    /// flows through the local Xray HTTP inbound while connected.
    ///
    /// The user's original proxy settings are snapshotted to a marker file before
    /// the first change; <see cref="RestoreIfMarkerExists"/> runs at startup so a
    /// crash never leaves the machine stuck behind a dead proxy.
    /// </summary>
    public static class SystemProxyService
    {
        private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

        private const string DefaultBypass =
            "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;" +
            "172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;" +
            "192.168.*;<local>";

        /// <summary>Built-in LAN bypass plus the user-defined exceptions from Settings.</summary>
        private static string BuildBypass()
        {
            var user = string.Join(';', ProxyBypassStore.Entries);
            return user.Length == 0 ? DefaultBypass : $"{DefaultBypass};{user}";
        }

        private sealed class Snapshot
        {
            public int ProxyEnable { get; set; }
            public string? ProxyServer { get; set; }
            public string? ProxyOverride { get; set; }
        }

        private static string MarkerPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lithiumvpn", "proxy-backup.json");

        public static void Enable(int httpPort)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true)
                ?? throw new InvalidOperationException("Cannot open Internet Settings registry key.");

            // Snapshot the pre-VPN state once (don't overwrite if we crashed mid-session).
            if (!File.Exists(MarkerPath))
            {
                var snap = new Snapshot
                {
                    ProxyEnable = (int)(key.GetValue("ProxyEnable") ?? 0),
                    ProxyServer = key.GetValue("ProxyServer") as string,
                    ProxyOverride = key.GetValue("ProxyOverride") as string,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
                File.WriteAllText(MarkerPath, JsonSerializer.Serialize(snap));
            }

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", $"127.0.0.1:{httpPort}", RegistryValueKind.String);
            key.SetValue("ProxyOverride", BuildBypass(), RegistryValueKind.String);

            NotifyWinInet();
        }

        public static void Disable()
        {
            Snapshot? snap = null;
            try
            {
                if (File.Exists(MarkerPath))
                    snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(MarkerPath));
            }
            catch { /* corrupt marker — fall back to plain disable */ }

            using (var key = Registry.CurrentUser.OpenSubKey(RegPath, writable: true))
            {
                if (key is not null)
                {
                    key.SetValue("ProxyEnable", snap?.ProxyEnable ?? 0, RegistryValueKind.DWord);

                    if (!string.IsNullOrEmpty(snap?.ProxyServer))
                        key.SetValue("ProxyServer", snap.ProxyServer, RegistryValueKind.String);
                    else
                        key.DeleteValue("ProxyServer", throwOnMissingValue: false);

                    if (!string.IsNullOrEmpty(snap?.ProxyOverride))
                        key.SetValue("ProxyOverride", snap.ProxyOverride, RegistryValueKind.String);
                }
            }

            try { File.Delete(MarkerPath); } catch { }
            NotifyWinInet();
        }

        /// <summary>
        /// Startup safety net: if a previous session crashed while the proxy was
        /// enabled, the marker file still exists — restore the original settings.
        /// </summary>
        public static void RestoreIfMarkerExists()
        {
            if (File.Exists(MarkerPath))
                Disable();
        }

        // ─── WinINET refresh ─────────────────────────────────────
        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(
            IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private static void NotifyWinInet()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
    }
}
