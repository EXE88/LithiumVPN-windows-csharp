using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// In-memory cache of the signed-in user's backend data.
    ///
    /// It is populated once — right after authentication (or, when a saved session
    /// exists, on the splash screen) — so every page renders from real data instead
    /// of demo placeholders. Pages read the cached datasets and may call
    /// <see cref="RefreshStatusAsync"/> / <see cref="RefreshTicketsAsync"/> to update
    /// a single slice after an action (e.g. buying a plan or opening a ticket).
    /// </summary>
    public sealed class AppState
    {
        private static readonly Lazy<AppState> _lazy = new(() => new AppState());
        public static AppState Instance => _lazy.Value;

        private AppState() { }

        // ─── Cached datasets ────────────────────────────────────────
        public UserStatusDto? Status { get; private set; }
        public IReadOnlyList<PlanDto> Plans { get; private set; } = Array.Empty<PlanDto>();
        public IReadOnlyList<EventDto> Events { get; private set; } = Array.Empty<EventDto>();
        public IReadOnlyList<TicketDto> Tickets { get; private set; } = Array.Empty<TicketDto>();

        /// <summary>Convenience: all purchases across the account (each holds its configs).</summary>
        public IReadOnlyList<PurchaseDto> Purchases =>
            Status?.Purchases ?? (IReadOnlyList<PurchaseDto>)Array.Empty<PurchaseDto>();

        /// <summary>Raised whenever any cached dataset changes so bound pages can re-render.</summary>
        public event Action? Changed;

        public void Clear()
        {
            // Logging out — drop the VPN tunnel along with the session.
            _ = Xray.ConnectionService.Instance.DisconnectAsync();
            HeartbeatService.Instance.Stop();
            Status = null;
            Plans = Array.Empty<PlanDto>();
            Events = Array.Empty<EventDto>();
            Tickets = Array.Empty<TicketDto>();
            Changed?.Invoke();
        }

        public enum LoadOutcome { Success, AuthFailed, NetworkError }

        private static LoadOutcome Classify<T>(ApiResult<T> r) =>
            r.IsNetworkError ? LoadOutcome.NetworkError
            : (r.StatusCode is 401 or 403) ? LoadOutcome.AuthFailed
            : LoadOutcome.NetworkError;

        /// <summary>
        /// Fetches every dataset the app needs before the dashboard is shown.
        /// Returns <see cref="LoadOutcome.Success"/> only when all critical calls
        /// succeed; the caller must NOT proceed on a non-success outcome so the app
        /// never loads with partial data.
        /// </summary>
        public async Task<LoadOutcome> LoadAllAsync()
        {
            var status = await ApiService.GetStatusAsync();
            if (!status.IsSuccess) return Classify(status);
            Status = status.Data;

            var plans = await ApiService.GetPlansAsync();
            if (!plans.IsSuccess) return Classify(plans);
            Plans = plans.Data?.Plans ?? (IReadOnlyList<PlanDto>)Array.Empty<PlanDto>();

            var events = await ApiService.GetRecentEventsAsync();
            if (!events.IsSuccess) return Classify(events);
            Events = events.Data?.Events ?? (IReadOnlyList<EventDto>)Array.Empty<EventDto>();

            var tickets = await ApiService.GetTicketsAsync();
            if (!tickets.IsSuccess) return Classify(tickets);
            Tickets = tickets.Data?.Tickets ?? (IReadOnlyList<TicketDto>)Array.Empty<TicketDto>();

            Changed?.Invoke();

            // Session is live — start the once-a-minute online heartbeat.
            HeartbeatService.Instance.Start();
            return LoadOutcome.Success;
        }

        /// <summary>Refreshes just the account status (username, email, coins, purchases).</summary>
        public async Task<bool> RefreshStatusAsync()
        {
            var status = await ApiService.GetStatusAsync();
            if (!status.IsSuccess) return false;
            Status = status.Data;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Refreshes just the ticket list.</summary>
        public async Task<bool> RefreshTicketsAsync()
        {
            var tickets = await ApiService.GetTicketsAsync();
            if (!tickets.IsSuccess) return false;
            Tickets = tickets.Data?.Tickets ?? (IReadOnlyList<TicketDto>)Array.Empty<TicketDto>();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Refreshes just the recent events (notifications).</summary>
        public async Task<bool> RefreshEventsAsync()
        {
            var events = await ApiService.GetRecentEventsAsync();
            if (!events.IsSuccess) return false;
            Events = events.Data?.Events ?? (IReadOnlyList<EventDto>)Array.Empty<EventDto>();
            Changed?.Invoke();
            return true;
        }
    }
}
