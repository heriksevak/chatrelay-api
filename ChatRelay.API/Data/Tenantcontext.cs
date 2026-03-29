// ============================================================
//  ChatRelay — TenantContext Implementation
// ============================================================

using ChatRelay.API.Data;
using Microsoft.EntityFrameworkCore;
using System;

namespace ChatRelay.API.Context;

public class TenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _db;

    public TenantContext(IHttpContextAccessor httpContextAccessor, ApplicationDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    private HttpContext Http =>
        _httpContextAccessor.HttpContext
        ?? throw new InvalidOperationException("TenantContext used outside of an HTTP request");

    // ── Claims ───────────────────────────────────────────────

    public bool IsAuthenticated =>
        Http.User.Identity?.IsAuthenticated == true;

    public Guid TenantId
    {
        get
        {
            var claim = Http.User.FindFirst("TenantId")?.Value;
            if (string.IsNullOrEmpty(claim))
                throw new UnauthorizedAccessException("TenantId claim missing from token");
            return Guid.Parse(claim);
        }
    }

    public Guid UserId
    {
        get
        {
            var claim = Http.User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(claim))
                throw new UnauthorizedAccessException("UserId claim missing from token");
            return Guid.Parse(claim);
        }
    }

    public string UserRole =>
        Http.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
        ?? string.Empty;

    public string UserEmail =>
        Http.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? string.Empty;

    public string FullName =>
        Http.User.FindFirst("FullName")?.Value
        ?? string.Empty;

    public bool IsSuperAdmin =>
        UserRole == "SuperAdmin";

    public bool IsTenantAdmin =>
        UserRole is "TenantAdmin" or "SuperAdmin";

    // ── WabaId — from header X-Waba-Id ──────────────────────
    // Client sends: X-Waba-Id: 3fa85f64-5717-4562-b3fc-2c963f66afa6

    public Guid? WabaId
    {
        get
        {
            var header = Http.Request.Headers["X-Waba-Id"].FirstOrDefault();
            if (string.IsNullOrEmpty(header)) return null;
            if (Guid.TryParse(header, out var wabaId)) return wabaId;
            return null;
        }
    }

    // ── WABA access validation ───────────────────────────────

    public async Task ValidateWabaAccessAsync(Guid wabaId)
    {
        // SuperAdmin can access any WABA
        if (IsSuperAdmin) return;

        var belongs = await _db.WabaAccounts
            .AnyAsync(w => w.Id == wabaId && w.TenantId == TenantId);

        if (!belongs)
            throw new UnauthorizedAccessException(
                $"WABA {wabaId} does not belong to tenant {TenantId}");
    }
}