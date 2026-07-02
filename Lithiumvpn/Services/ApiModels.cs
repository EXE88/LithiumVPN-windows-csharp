using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lithiumvpn.Services
{
    // ─────────────────────────────────────────────────────────────
    //  Standard response envelope used by the backend:
    //      { "success": bool, "message": string?, "data": {…}?, "errors": {…}? }
    // ─────────────────────────────────────────────────────────────
    public sealed class ApiEnvelope<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("data")] public T? Data { get; set; }
        [JsonPropertyName("errors")] public JsonElement? Errors { get; set; }
    }

    /// <summary>
    /// Uniform result returned by every <see cref="ApiService"/> call.
    /// Callers check <see cref="IsSuccess"/>; on failure <see cref="Message"/>
    /// carries a user-displayable reason and <see cref="IsNetworkError"/> tells
    /// whether the backend was unreachable.
    /// </summary>
    public sealed class ApiResult<T>
    {
        public bool IsSuccess { get; init; }
        public int StatusCode { get; init; }
        public T? Data { get; init; }
        public string? Message { get; init; }
        public JsonElement? Errors { get; init; }
        public bool IsNetworkError { get; init; }

        public static ApiResult<T> Ok(T? data, int status, string? message) =>
            new() { IsSuccess = true, Data = data, StatusCode = status, Message = message };

        public static ApiResult<T> Fail(int status, string? message, JsonElement? errors = null) =>
            new() { IsSuccess = false, StatusCode = status, Message = message, Errors = errors };

        public static ApiResult<T> Network(string message) =>
            new() { IsSuccess = false, StatusCode = 0, Message = message, IsNetworkError = true };
    }

    // ─────────────────────────────────────────────────────────────
    //  Auth / token DTOs
    // ─────────────────────────────────────────────────────────────
    public sealed class TokenPairDto
    {
        [JsonPropertyName("access")] public string? Access { get; set; }
        [JsonPropertyName("refresh")] public string? Refresh { get; set; }
    }

    public sealed class TokenRefreshDto
    {
        [JsonPropertyName("access")] public string? Access { get; set; }
        [JsonPropertyName("refresh")] public string? Refresh { get; set; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Account status
    // ─────────────────────────────────────────────────────────────
    public sealed class UserStatusDto
    {
        [JsonPropertyName("username")] public string? Username { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }
        [JsonPropertyName("coin_count")] public int CoinCount { get; set; }
        [JsonPropertyName("purchases")] public List<PurchaseDto>? Purchases { get; set; }
    }

    /// <summary>One purchase (= one bought plan). All its configs share the plan.</summary>
    public sealed class PurchaseDto
    {
        [JsonPropertyName("purchase_id")] public int PurchaseId { get; set; }
        [JsonPropertyName("plan")] public string? Plan { get; set; }
        [JsonPropertyName("configs")] public List<ConfigDto>? Configs { get; set; }
    }

    /// <summary>A single VPN config (one server/location) inside a purchase.</summary>
    public sealed class ConfigDto
    {
        [JsonPropertyName("config_code")] public string? ConfigCode { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
        [JsonPropertyName("gb_left")] public string? GbLeft { get; set; }
        [JsonPropertyName("days_left")] public int DaysLeft { get; set; }
        [JsonPropertyName("client_id")] public string? ClientId { get; set; }
        [JsonPropertyName("email")] public string? Email { get; set; }

        /// <summary>Remaining data in GB (the API sends it as a string like "1.00").</summary>
        public double GbLeftValue =>
            double.TryParse(GbLeft, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  Events (notifications)
    // ─────────────────────────────────────────────────────────────
    public sealed class EventDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("topic")] public string? Topic { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    }

    public sealed class EventListDto
    {
        [JsonPropertyName("events")] public List<EventDto>? Events { get; set; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Plans (plan objects are open-ended in the schema, kept as raw JSON)
    // ─────────────────────────────────────────────────────────────
    public sealed class PlanListDto
    {
        [JsonPropertyName("plans")] public List<PlanDto>? Plans { get; set; }
    }

    /// <summary>A plan offered on the Plans page.</summary>
    public sealed class PlanDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("plan_name")] public string? PlanName { get; set; }
        [JsonPropertyName("servers")] public List<string>? Servers { get; set; }
        [JsonPropertyName("number_of_users")] public int NumberOfUsers { get; set; }
        [JsonPropertyName("time")] public int Time { get; set; }   // validity in months
        [JsonPropertyName("usage")] public int Usage { get; set; } // data in GB
        [JsonPropertyName("price")] public int Price { get; set; } // cost in coins

        /// <summary>Number of server locations included.</summary>
        public int LocationCount => Servers?.Count ?? 0;
    }

    public sealed class BuyPlanDto
    {
        [JsonPropertyName("plan_name")] public string? PlanName { get; set; }
        [JsonPropertyName("number_of_users")] public int NumberOfUsers { get; set; }
        [JsonPropertyName("time")] public int Time { get; set; }
        [JsonPropertyName("usage")] public int Usage { get; set; }
        [JsonPropertyName("price")] public int Price { get; set; }
        [JsonPropertyName("purchases")] public List<PurchaseDto>? Purchases { get; set; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Tickets
    // ─────────────────────────────────────────────────────────────
    public sealed class TicketListDto
    {
        [JsonPropertyName("tickets")] public List<TicketDto>? Tickets { get; set; }
    }

    public sealed class TicketDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("subject")] public string? Subject { get; set; }
        [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
        [JsonPropertyName("unread")] public int Unread { get; set; }
    }

    public sealed class TicketCreateDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("subject")] public string? Subject { get; set; }
        [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    }

    public sealed class TicketMessagesDto
    {
        [JsonPropertyName("ticket_id")] public int TicketId { get; set; }
        [JsonPropertyName("messages")] public List<TicketMessageDto>? Messages { get; set; }
    }

    public sealed class TicketMessageDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }   // "client" | "admin"
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }

        public bool IsClient => string.Equals(From, "client", StringComparison.OrdinalIgnoreCase);
    }
}
