// ============================================================
//  ChatRelay — ApiKeyMiddleware (updated)
//  Now validates X-Api-Key against the ApiKeys table
//  with proper scope checking, rate limiting awareness,
//  and tenant context injection
// ============================================================

using ChatRelay.Models;
using ChatRelay.API.Services;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using ChatRelay.API.DTOs;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly HashSet<string> _publicRoutes = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/api/auth/login",
        "/api/auth/refresh",
        "/api/webhook",
        "/api/tenant/resolve",
        "/api/tenant/resolve/slug",
        "/api/health",
        "/swagger",
    };

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IApiKeyService apiKeyService)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 1. Always bypass public routes + swagger
        if (_publicRoutes.Any(r =>
                path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // 2. Swagger UI
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 3. Bearer JWT — skip API key check, JWT middleware handles auth
        var authHeader = context.Request.Headers["Authorization"]
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 4. API key path — require X-Api-Key header
        if (!context.Request.Headers.TryGetValue("X-Api-Key",
                out var extractedApiKey) ||
            string.IsNullOrWhiteSpace(extractedApiKey))
        {
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized,
                "Authentication required. Use Bearer token or X-Api-Key header.");
            return;
        }

        // 5. Validate key against DB
        var validation = await apiKeyService
            .ValidateKeyAsync(extractedApiKey.ToString());

        if (validation == null)
        {
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized,
                "Invalid or expired API key.");
            return;
        }

        // 6. Inject tenant context so TenantMiddleware and controllers
        //    can use it just like JWT auth
        context.Items["TenantId"]          = validation.TenantId;
        context.Items["ApiKeyId"]          = validation.ApiKeyId;
        context.Items["ApiKeyScopes"]      = validation.Scopes;
        context.Items["ApiKeyWabaId"]      = validation.WabaId;
        context.Items["RateLimitPerMin"]   = validation.RateLimitPerMinute;

        // Track IP for usage stats
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "";
        context.Items["ClientIp"] = ip;

        // 7. Scope check for sensitive endpoints
        var scopeError = CheckScopeForPath(path,
            context.Request.Method, validation);

        if (scopeError != null)
        {
            await WriteErrorAsync(context, HttpStatusCode.Forbidden, scopeError);
            return;
        }

        await _next(context);
    }

    // ── Scope enforcement per route ───────────────────────────

    private static string? CheckScopeForPath(
        string path, string method, ApiKeyValidationResult validation)
    {
        // Messages
        if (path.StartsWith("/api/messages", StringComparison.OrdinalIgnoreCase))
        {
            if (method == "POST" && !validation.HasScope(ApiKeyScopes.MessagesSend))
                return "This API key does not have the 'messages:send' scope";
            if (method == "GET" && !validation.HasScope(ApiKeyScopes.MessagesRead))
                return "This API key does not have the 'messages:read' scope";
        }

        // Contacts
        if (path.StartsWith("/api/contacts", StringComparison.OrdinalIgnoreCase))
        {
            if ((method == "POST" || method == "PUT" || method == "DELETE") &&
                !validation.HasScope(ApiKeyScopes.ContactsWrite))
                return "This API key does not have the 'contacts:write' scope";
            if (method == "GET" && !validation.HasScope(ApiKeyScopes.ContactsRead))
                return "This API key does not have the 'contacts:read' scope";
        }

        // Templates
        if (path.StartsWith("/api/templates", StringComparison.OrdinalIgnoreCase))
        {
            if ((method == "POST" || method == "PUT" || method == "DELETE") &&
                !validation.HasScope(ApiKeyScopes.TemplatesWrite))
                return "This API key does not have the 'templates:write' scope";
            if (method == "GET" && !validation.HasScope(ApiKeyScopes.TemplatesRead))
                return "This API key does not have the 'templates:read' scope";
        }

        // WABAs
        if (path.StartsWith("/api/waba", StringComparison.OrdinalIgnoreCase))
        {
            if (method == "GET" && !validation.HasScope(ApiKeyScopes.WabasRead))
                return "This API key does not have the 'wabas:read' scope";
        }

        // Webhooks
        if (path.StartsWith("/api/webhooks", StringComparison.OrdinalIgnoreCase))
        {
            if ((method == "POST" || method == "PUT" || method == "DELETE") &&
                !validation.HasScope(ApiKeyScopes.WebhooksWrite))
                return "This API key does not have the 'webhooks:write' scope";
            if (method == "GET" && !validation.HasScope(ApiKeyScopes.WebhooksRead))
                return "This API key does not have the 'webhooks:read' scope";
        }

        // API key management — never accessible via API key itself
        if (path.StartsWith("/api/apikeys", StringComparison.OrdinalIgnoreCase))
            return "API key management requires JWT authentication";

        return null;
    }

    private static async Task WriteErrorAsync(
        HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode  = (int)statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            success    = false,
            message,
            statusCode = (int)statusCode
        }));
    }
}

public static class ApiKeyMiddlewareExtensions
{
    public static IApplicationBuilder UseApiKeyMiddleware(
        this IApplicationBuilder app)
        => app.UseMiddleware<ApiKeyMiddleware>();
}
