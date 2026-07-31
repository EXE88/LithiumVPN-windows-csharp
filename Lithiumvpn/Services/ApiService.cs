using System.Text.Json;
using System.Threading.Tasks;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// High-level, strongly-typed facade over every backend endpoint used by the app.
    /// UI code should call these methods and inspect the returned <see cref="ApiResult{T}"/>.
    /// </summary>
    public static class ApiService
    {
        private static ApiClient Api => ApiClient.Instance;
        private static TokenStore Tokens => TokenStore.Instance;

        public static bool IsAuthenticated => Tokens.HasSession;

        /// <summary>Lightweight probe used by the splash screen to confirm the backend is reachable.</summary>
        public static Task<bool> CheckConnectivityAsync() => Api.IsBackendReachableAsync();

        /// <summary>Probe with a short cap so startup falls back to offline instead of hanging.</summary>
        public static Task<bool> CheckConnectivityAsync(System.TimeSpan timeout) =>
            Api.IsBackendReachableAsync(timeout);

        // ══════════════════════════════════════════════════════════
        //  Authentication
        // ══════════════════════════════════════════════════════════

        /// <summary>POST /api/token/ — obtain and persist the access/refresh pair.</summary>
        public static async Task<ApiResult<TokenPairDto>> LoginAsync(string username, string password)
        {
            var result = await Api.PostRawAsync<TokenPairDto>(
                "api/token/", new { username, password }, auth: false);

            if (result.IsSuccess && result.Data is not null)
                Tokens.SetTokens(result.Data.Access, result.Data.Refresh);

            return result;
        }

        /// <summary>POST /accounts/register/</summary>
        public static Task<ApiResult<JsonElement>> RegisterAsync(string username, string email, string password) =>
            Api.PostAsync<JsonElement>("accounts/register/", new { username, email, password }, auth: false);

        /// <summary>POST /accounts/verify-email/</summary>
        public static Task<ApiResult<JsonElement>> VerifyEmailAsync(string email, string code) =>
            Api.PostAsync<JsonElement>("accounts/verify-email/", new { email, code }, auth: false);

        /// <summary>POST /accounts/resend-verification/</summary>
        public static Task<ApiResult<JsonElement>> ResendVerificationAsync(string email) =>
            Api.PostAsync<JsonElement>("accounts/resend-verification/", new { email }, auth: false);

        /// <summary>POST /accounts/logout/ — invalidates the refresh token, then clears local session.</summary>
        public static async Task<ApiResult<JsonElement>> LogoutAsync()
        {
            var refresh = Tokens.RefreshToken;
            ApiResult<JsonElement> result;
            if (!string.IsNullOrWhiteSpace(refresh))
                result = await Api.PostAsync<JsonElement>("accounts/logout/", new { refresh });
            else
                result = ApiResult<JsonElement>.Ok(default, 205, null);

            Tokens.Clear();
            return result;
        }

        /// <summary>GET /accounts/status/</summary>
        public static Task<ApiResult<UserStatusDto>> GetStatusAsync() =>
            Api.GetAsync<UserStatusDto>("accounts/status/");

        // ══════════════════════════════════════════════════════════
        //  Health
        // ══════════════════════════════════════════════════════════
        public static Task<ApiResult<JsonElement>> HealthWindowsAsync() =>
            Api.GetAsync<JsonElement>("health/windows/");

        // ══════════════════════════════════════════════════════════
        //  Plans
        // ══════════════════════════════════════════════════════════
        public static Task<ApiResult<PlanListDto>> GetPlansAsync() =>
            Api.GetAsync<PlanListDto>("plans/");

        public static Task<ApiResult<BuyPlanDto>> BuyPlanAsync(int planId) =>
            Api.PostAsync<BuyPlanDto>("plans/buy/", new { plan_id = planId });

        // ══════════════════════════════════════════════════════════
        //  Events (notifications)
        // ══════════════════════════════════════════════════════════
        public static Task<ApiResult<EventListDto>> GetNewEventsAsync() =>
            Api.GetAsync<EventListDto>("events/new/");

        public static Task<ApiResult<EventListDto>> GetRecentEventsAsync() =>
            Api.GetAsync<EventListDto>("events/recent/");

        public static Task<ApiResult<JsonElement>> MarkEventsCheckedAsync() =>
            Api.PostAsync<JsonElement>("events/checked/");

        // ══════════════════════════════════════════════════════════
        //  Tickets (client side)
        // ══════════════════════════════════════════════════════════
        public static Task<ApiResult<TicketListDto>> GetTicketsAsync() =>
            Api.GetAsync<TicketListDto>("tickets/list/");

        public static Task<ApiResult<TicketCreateDto>> CreateTicketAsync(string subject) =>
            Api.PostAsync<TicketCreateDto>("tickets/create/", new { subject });

        public static Task<ApiResult<TicketMessagesDto>> GetTicketMessagesAsync(int ticketId) =>
            Api.GetAsync<TicketMessagesDto>($"tickets/{ticketId}/messages/");

        public static Task<ApiResult<JsonElement>> SendTicketMessageAsync(int ticketId, string message) =>
            Api.PostAsync<JsonElement>($"tickets/{ticketId}/messages/client/", new { message });

        public static Task<ApiResult<JsonElement>> MarkTicketSeenAsync(int ticketId) =>
            Api.PostAsync<JsonElement>($"tickets/{ticketId}/seen/client/");
    }
}
