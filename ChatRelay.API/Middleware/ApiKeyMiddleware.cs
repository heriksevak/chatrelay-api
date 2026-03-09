using ChatRelay.API.Data;
using Microsoft.EntityFrameworkCore;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext db)
    {
        var path = context.Request.Path.Value?.ToLower();

        if (path == "/" || path.StartsWith("/webhook"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("API Key missing");
            return;
        }

        var tenant = await db.Tenants
            .FirstOrDefaultAsync(t => t.ApiKey == extractedApiKey);

        if (tenant == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid API Key");
            return;
        }


        context.Items["Tenant"] = tenant;

        await _next(context);
    }
}