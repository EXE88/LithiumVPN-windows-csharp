using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Lithiumvpn.Services
{
    /// <summary>
    /// Core HTTP transport for the backend API.
    ///
    /// Responsibilities:
    ///  • attaches the JWT bearer token to authenticated requests,
    ///  • transparently refreshes an expired access token (once) on a 401 and retries,
    ///  • normalizes every response into an <see cref="ApiResult{T}"/>.
    ///
    /// Two body styles are supported:
    ///  • <b>enveloped</b> endpoints return <c>{success,message,data,errors}</c>,
    ///  • <b>raw</b> endpoints (the SimpleJWT token endpoints) return the object directly.
    /// </summary>
    public sealed class ApiClient
    {
        private static readonly Lazy<ApiClient> _lazy = new(() => new ApiClient());
        public static ApiClient Instance => _lazy.Value;

        private readonly HttpClient _http;
        private readonly TokenStore _tokens = TokenStore.Instance;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private ApiClient()
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(AppConfig.ApiBaseUrl + "/"),
                Timeout = AppConfig.Timeout
            };
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ─────────────────────────────────────────────────────────
        //  Public verb helpers — enveloped responses
        // ─────────────────────────────────────────────────────────
        public Task<ApiResult<T>> GetAsync<T>(string path, bool auth = true) =>
            SendEnvelopedAsync<T>(HttpMethod.Get, path, null, auth);

        public Task<ApiResult<T>> PostAsync<T>(string path, object? body = null, bool auth = true) =>
            SendEnvelopedAsync<T>(HttpMethod.Post, path, body, auth);

        // ─────────────────────────────────────────────────────────
        //  Public verb helpers — raw (non-enveloped) responses
        // ─────────────────────────────────────────────────────────
        public Task<ApiResult<T>> PostRawAsync<T>(string path, object? body = null, bool auth = false) =>
            SendRawAsync<T>(HttpMethod.Post, path, body, auth);

        // ─────────────────────────────────────────────────────────
        //  Connectivity probe — any HTTP response (even 401/403) proves
        //  the backend is reachable; only a transport failure means "down".
        // ─────────────────────────────────────────────────────────
        public async Task<bool> IsBackendReachableAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/schema/");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Core send + one-shot refresh-on-401
        // ─────────────────────────────────────────────────────────
        private async Task<HttpResponseMessage?> SendCoreAsync(
            HttpMethod method, string path, object? body, bool auth, bool allowRetry = true)
        {
            var request = BuildRequest(method, path, body, auth);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request);
            }
            catch
            {
                return null; // network / timeout — surfaced as a network error
            }

            // If authenticated and unauthorized, try a single refresh + retry.
            if (auth && response.StatusCode == HttpStatusCode.Unauthorized && allowRetry
                && _tokens.HasSession)
            {
                response.Dispose();
                var refreshed = await TryRefreshAsync();
                if (refreshed)
                    return await SendCoreAsync(method, path, body, auth, allowRetry: false);

                // Refresh failed → session is dead.
                _tokens.Clear();
                return await SendCoreAsync(method, path, body, auth: false, allowRetry: false);
            }

            return response;
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string path, object? body, bool auth)
        {
            var request = new HttpRequestMessage(method, path);

            if (body is not null)
            {
                var json = JsonSerializer.Serialize(body, JsonOpts);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            if (auth && !string.IsNullOrWhiteSpace(_tokens.AccessToken))
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _tokens.AccessToken);

            return request;
        }

        // ─────────────────────────────────────────────────────────
        //  Enveloped deserialization
        // ─────────────────────────────────────────────────────────
        private async Task<ApiResult<T>> SendEnvelopedAsync<T>(
            HttpMethod method, string path, object? body, bool auth)
        {
            var response = await SendCoreAsync(method, path, body, auth);
            if (response is null)
                return ApiResult<T>.Network("Could not reach the server. Check your connection or the backend URL.");

            using (response)
            {
                var raw = await SafeReadAsync(response);

                ApiEnvelope<T>? env = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(raw))
                        env = JsonSerializer.Deserialize<ApiEnvelope<T>>(raw, JsonOpts);
                }
                catch { /* non-JSON body */ }

                bool ok = response.IsSuccessStatusCode && (env?.Success ?? true);
                if (ok)
                    return ApiResult<T>.Ok(env is not null ? env.Data : default, (int)response.StatusCode, env?.Message);

                return ApiResult<T>.Fail(
                    (int)response.StatusCode,
                    ExtractError(env, response.StatusCode),
                    env?.Errors);
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Raw deserialization (token endpoints)
        // ─────────────────────────────────────────────────────────
        private async Task<ApiResult<T>> SendRawAsync<T>(
            HttpMethod method, string path, object? body, bool auth)
        {
            var response = await SendCoreAsync(method, path, body, auth);
            if (response is null)
                return ApiResult<T>.Network("Could not reach the server. Check your connection or the backend URL.");

            using (response)
            {
                var raw = await SafeReadAsync(response);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var data = string.IsNullOrWhiteSpace(raw)
                            ? default
                            : JsonSerializer.Deserialize<T>(raw, JsonOpts);
                        return ApiResult<T>.Ok(data, (int)response.StatusCode, null);
                    }
                    catch
                    {
                        return ApiResult<T>.Fail((int)response.StatusCode, "Unexpected response from server.");
                    }
                }

                // Error bodies from token endpoints usually carry a "detail" field.
                return ApiResult<T>.Fail((int)response.StatusCode, ExtractRawError(raw, response.StatusCode));
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Token refresh
        // ─────────────────────────────────────────────────────────
        private async Task<bool> TryRefreshAsync()
        {
            if (!_tokens.HasSession) return false;

            await _refreshLock.WaitAsync();
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "api/token/refresh/")
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new { refresh = _tokens.RefreshToken }, JsonOpts),
                        Encoding.UTF8, "application/json")
                };

                HttpResponseMessage response;
                try { response = await _http.SendAsync(request); }
                catch { return false; }

                using (response)
                {
                    if (!response.IsSuccessStatusCode) return false;
                    var raw = await SafeReadAsync(response);
                    var dto = JsonSerializer.Deserialize<TokenRefreshDto>(raw, JsonOpts);
                    if (dto is null || string.IsNullOrWhiteSpace(dto.Access)) return false;

                    _tokens.SetTokens(dto.Access, dto.Refresh); // handles rotation if present
                    return true;
                }
            }
            catch { return false; }
            finally { _refreshLock.Release(); }
        }

        // ─────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────
        private static async Task<string> SafeReadAsync(HttpResponseMessage response)
        {
            try { return await response.Content.ReadAsStringAsync(); }
            catch { return string.Empty; }
        }

        private static string ExtractError<T>(ApiEnvelope<T>? env, HttpStatusCode status)
        {
            if (!string.IsNullOrWhiteSpace(env?.Message))
                return env!.Message!;

            if (env?.Errors is JsonElement e && e.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in e.EnumerateObject())
                {
                    var text = FirstString(prop.Value);
                    if (!string.IsNullOrWhiteSpace(text)) return text!;
                }
            }
            return DefaultStatusMessage(status);
        }

        private static string ExtractRawError(string raw, HttpStatusCode status)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                            return detail.GetString()!;
                        if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                            return msg.GetString()!;
                        foreach (var prop in root.EnumerateObject())
                        {
                            var text = FirstString(prop.Value);
                            if (!string.IsNullOrWhiteSpace(text)) return text!;
                        }
                    }
                }
            }
            catch { /* fall through */ }
            return DefaultStatusMessage(status);
        }

        private static string? FirstString(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return value.GetString();
                case JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray())
                    {
                        var s = FirstString(item);
                        if (!string.IsNullOrWhiteSpace(s)) return s;
                    }
                    return null;
                default:
                    return null;
            }
        }

        private static string DefaultStatusMessage(HttpStatusCode status) => status switch
        {
            HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
            HttpStatusCode.Forbidden => "You don't have permission to do that.",
            HttpStatusCode.NotFound => "The requested item was not found.",
            (HttpStatusCode)429 => "Too many attempts. Please wait a moment and try again.",
            HttpStatusCode.BadGateway => "The server is temporarily unavailable. Please try again.",
            HttpStatusCode.InternalServerError => "Something went wrong on the server. Please try again later.",
            _ => "Something went wrong. Please try again."
        };
    }
}
