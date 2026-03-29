// ============================================================
//  ChatRelay — TenantMiddleware
//  Runs on every authenticated request.
//  Validates tenant exists, is active, and not on expired trial.
// ============================================================

using ChatRelay.API.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Net;
using System.Text.Json;

namespace ChatRelay.API.Middleware;

public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    // Routes that bypass tenant validation entirely
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


    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip public routes
        if (_publicRoutes.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        // Skip if not authenticated (let [Authorize] handle it)
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        // Extract TenantId from JWT
        var tenantIdClaim = context.User.FindFirst("TenantId")?.Value;
        if (string.IsNullOrEmpty(tenantIdClaim) || !Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized, "Invalid tenant claim in token");
            return;
        }

        // Load and validate tenant
        var tenant = await db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId);

        if (tenant == null)
        {
            await WriteErrorAsync(context, HttpStatusCode.Unauthorized, "Tenant not found");
            return;
        }

        if (!tenant.IsActive)
        {
            await WriteErrorAsync(context, HttpStatusCode.Forbidden,
                "Your account has been suspended. Please contact support.");
            return;
        }

        if (tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt < DateTime.UtcNow
            && tenant.PlanType == ChatRelay.Models.PlanType.Free)
        {
            await WriteErrorAsync(context, HttpStatusCode.PaymentRequired,
                "Your free trial has expired. Please upgrade your plan.");
            return;
        }

        // Attach tenant info to HttpContext.Items for use downstream
        context.Items["Tenant"] = tenant;
        context.Items["TenantId"] = tenantId;

        await _next(context);
    }

    private static async Task WriteErrorAsync(
        HttpContext context, HttpStatusCode statusCode, string message)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var response = JsonSerializer.Serialize(new
        {
            success = false,
            message,
            statusCode = (int)statusCode
        });

        await context.Response.WriteAsync(response);
    }
}

// Extension method for clean registration in Program.cs
public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantMiddleware(this IApplicationBuilder app)
        => app.UseMiddleware<TenantMiddleware>();
}