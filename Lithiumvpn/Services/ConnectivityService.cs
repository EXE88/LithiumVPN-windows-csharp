using System;
using System.Threading.Tasks;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// App-wide "is the backend usable" flag.
    ///
    /// The app is allowed to run fully offline (local/imported configs still work),
    /// so this is separate from the VPN tunnel state. It flips to <c>true</c> only
    /// once the backend was reached AND the signed-in user's data was loaded
    /// (see <see cref="AppState.LoadAllAsync"/>). Backend-only pages/sections observe
    /// <see cref="StateChanged"/> and render a "reconnect" placeholder while offline.
    /// </summary>
    public sealed class ConnectivityService
    {
        private static readonly Lazy<ConnectivityService> _lazy = new(() => new ConnectivityService());
        public static ConnectivityService Instance => _lazy.Value;

        private ConnectivityService() { }

        /// <summary>True when the backend is reachable and the account data is loaded.</summary>
        public bool IsOnline { get; private set; }

        /// <summary>Raised (on the caller's thread) whenever <see cref="IsOnline"/> changes.</summary>
        public event Action<bool>? StateChanged;

        public void SetOnline(bool online)
        {
            if (IsOnline == online) return;
            IsOnline = online;
            StateChanged?.Invoke(online);
        }

        public enum ReconnectOutcome { WentOnline, NeedLogin, StillOffline }

        /// <summary>
        /// Probes the backend and, when reachable, tries to (re)load the user's data.
        ///  • WentOnline   — reachable + data loaded (a saved session existed).
        ///  • NeedLogin    — reachable but there is no valid session; caller shows login.
        ///  • StillOffline — backend unreachable, or the data load failed mid-way.
        /// </summary>
        public async Task<ReconnectOutcome> TryReconnectAsync()
        {
            bool reachable = await ApiClient.Instance.IsBackendReachableAsync(TimeSpan.FromSeconds(3));
            if (!reachable) return ReconnectOutcome.StillOffline;

            if (!TokenStore.Instance.HasSession)
                return ReconnectOutcome.NeedLogin;

            var outcome = await AppState.Instance.LoadAllAsync();
            return outcome switch
            {
                AppState.LoadOutcome.Success => ReconnectOutcome.WentOnline,   // LoadAllAsync sets IsOnline
                AppState.LoadOutcome.AuthFailed => ReconnectOutcome.NeedLogin,
                _ => ReconnectOutcome.StillOffline,
            };
        }
    }
}
