using ChatRelay.API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

public class ApiKeyMiddlewareOld
{
    private readonly RequestDelegate _next;

    // Routes that bypass ALL auth checks
    private static readonly HashSet<string> _publicRoutes = new(StringComparer.OrdinalIgnoreCase)
{
    "/api/auth/login",
    "/api/auth/refresh",
    "/api/webhook",
    "/api/tenant/resolve",     // ← add this
    "/api/tenant/resolve/slug", // ← and this
    "/api/health",
    "/",
};

    public ApiKeyMiddlewareOld(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // 1. Always bypass public routes
        if (_publicRoutes.Any(r =>
                path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // 2. Always bypass Swagger
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 3. If request has a Bearer JWT token — skip API key check
        // JWT auth middleware already handles these requests
        var authHeader = context.Request.Headers["Authorization"]
            .FirstOrDefault();

        if (!string.IsNullOrEmpty(authHeader) &&
            authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // 4. No Bearer token — require X-Api-Key header
        // This path is for tenants calling your API programmatically
        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var extractedApiKey)
            || string.IsNullOrWhiteSpace(extractedApiKey))
        {
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"success\":false,\"message\":\"Authentication required. " +
                "Use Bearer token or X-Api-Key header.\"}");
            return;
        }

        // 5. Validate API key against ApiKeys table
        // TODO: uncomment this when ApiKey management endpoints are built
        // var hashedKey = HashApiKey(extractedApiKey.ToString());
        // var apiKey = await db.ApiKeys
        //     .Include(k => k.Tenant)
        //     .FirstOrDefaultAsync(k =>
        //         k.KeyHash == hashedKey &&
        //         k.IsActive &&
        //         (k.ExpiresAt == null || k.ExpiresAt > DateTime.UtcNow));
        //
        // if (apiKey == null)
        // {
        //     context.Response.StatusCode = 401;
        //     await context.Response.WriteAsync(
        //         "{\"success\":false,\"message\":\"Invalid or expired API key\"}");
        //     return;
        // }
        //
        // context.Items["TenantId"] = apiKey.TenantId;
        // context.Items["ApiKeyId"] = apiKey.Id;

        // For now — pass through (API key management not built yet)
        await _next(context);
    }

    // private static string HashApiKey(string key)
    // {
    //     var bytes = System.Security.Cryptography.SHA256.HashData(
    //         System.Text.Encoding.UTF8.GetBytes(key));
    //     return Convert.ToBase64String(bytes);
    // }
}

//using ChatRelay.API.Data;
//using Microsoft.EntityFrameworkCore;

//public class ApiKeyMiddleware
//{
//    private readonly RequestDelegate _next;

//    public ApiKeyMiddleware(RequestDelegate next)
//    {
//        _next = next;
//    }

//    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
//    {
//        var path = context.Request.Path.Value?.ToLower();

//        if (path == "/" || path.StartsWith("/webhook"))
//        {
//            await _next(context);
//            return;
//        }

//        if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey))
//        {
//            context.Response.StatusCode = 401;
//            await context.Response.WriteAsync("API Key missing");
//            return;
//        }

//        var apiKey = extractedApiKey.ToString();

//        var tenant = await db.Tenants
//            .FirstOrDefaultAsync(t => t.BillingAddress == apiKey);

//        if (tenant == null)
//        {
//            context.Response.StatusCode = 401;
//            await context.Response.WriteAsync("Invalid API Key");
//            return;
//        }

//        context.Items["Tenant"] = tenant;

//        await _next(context);
//    }
//}