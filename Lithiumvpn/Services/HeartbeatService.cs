using System;
using System.Threading;
using System.Threading.Tasks;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// Once-a-minute background heartbeat for a signed-in session:
    ///  • GET /health/windows/ — registers this client as online on the backend,
    ///  • GET /accounts/status/ — keeps the cached user data (coins, purchases) fresh.
    ///
    /// Started after a successful <see cref="AppState.LoadAllAsync"/> and stopped
    /// when the session is cleared (logout / dead refresh token). Failures are
    /// silent — the next tick simply tries again.
    /// </summary>
    public sealed class HeartbeatService
    {
        private static readonly Lazy<HeartbeatService> _lazy = new(() => new HeartbeatService());
        public static HeartbeatService Instance => _lazy.Value;

        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

        private Timer? _timer;
        private int _ticking;   // guards against overlapping ticks on a slow network

        private HeartbeatService() { }

        public void Start()
        {
            if (_timer is not null) return;
            _timer = new Timer(_ => _ = TickAsync(), null, Interval, Interval);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private async Task TickAsync()
        {
            if (!TokenStore.Instance.HasSession) return;
            if (Interlocked.Exchange(ref _ticking, 1) == 1) return;

            try
            {
                await ApiService.HealthWindowsAsync();           // report "online"
                await AppState.Instance.RefreshStatusAsync();    // coins, purchases, configs
                await AppState.Instance.RefreshEventsAsync();    // notifications
                await AppState.Instance.RefreshTicketsAsync();   // support tickets
            }
            catch { /* transient failures are retried on the next tick */ }
            finally
            {
                Interlocked.Exchange(ref _ticking, 0);
            }
        }
    }
}
