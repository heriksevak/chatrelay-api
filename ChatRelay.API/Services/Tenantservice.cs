// ============================================================
//  ChatRelay — ITenantService + TenantService
//  SuperAdmin only — create and manage tenants
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace ChatRelay.API.Services;

public interface ITenantService
{
    Task<ServiceResult<TenantResponse>> CreateTenantAsync(CreateTenantRequest request);
    Task<ServiceResult<List<TenantResponse>>> GetAllTenantsAsync();
    Task<ServiceResult<TenantResponse>> GetTenantByIdAsync(Guid tenantId);
    Task<ServiceResult<TenantResponse>> UpdateTenantAsync(Guid tenantId, CreateTenantRequest request);
    Task<ServiceResult<bool>> DeactivateTenantAsync(Guid tenantId);
}

public class TenantService : ITenantService
{
    private readonly ApplicationDbContext _db;

    public TenantService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ── Create Tenant ─────────────────────────────────────────

    public async Task<ServiceResult<TenantResponse>> CreateTenantAsync(
        CreateTenantRequest request)
    {
        // Generate slug from name if not provided
        var slug = string.IsNullOrWhiteSpace(request.Slug)
            ? GenerateSlug(request.Name)
            : GenerateSlug(request.Slug);

        // Slug must be unique
        var slugExists = await _db.Tenants
            .AnyAsync(t => t.Slug == slug);

        if (slugExists)
            return ServiceResult<TenantResponse>.Fail(
                $"Slug '{slug}' is already taken. Choose a different name.");

        // Custom domain must be unique
        if (!string.IsNullOrWhiteSpace(request.CustomDomain))
        {
            var domainExists = await _db.Tenants
                .AnyAsync(t => t.CustomDomain == request.CustomDomain);

            if (domainExists)
                return ServiceResult<TenantResponse>.Fail(
                    "This custom domain is already registered.");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Slug = slug,
            PlanType = request.PlanType,
            IsActive = true,
            MaxWabas = request.MaxWabas,
            MaxUsersPerWaba = request.MaxUsersPerWaba,
            MaxMessagesPerMonth = request.MaxMessagesPerMonth,
            IsWhiteLabel = request.IsWhiteLabel,
            BrandName = request.BrandName,
            BrandPrimaryColor = request.BrandPrimaryColor,
            CustomDomain = string.IsNullOrWhiteSpace(request.CustomDomain)
                                    ? null : request.CustomDomain.ToLower().Trim(),
            BillingEmail = request.BillingEmail,
            Country = request.Country,
            Timezone = request.Timezone ?? "UTC",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        // Set trial period for free plans
        if (request.PlanType == PlanType.Free)
            tenant.TrialEndsAt = DateTime.UtcNow.AddDays(14);

        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        return ServiceResult<TenantResponse>.Ok(ToResponse(tenant));
    }

    // ── Get All Tenants ───────────────────────────────────────

    public async Task<ServiceResult<List<TenantResponse>>> GetAllTenantsAsync()
    {
        var tenants = await _db.Tenants
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => ToResponse(t))
            .ToListAsync();

        return ServiceResult<List<TenantResponse>>.Ok(tenants);
    }

    // ── Get Single Tenant ─────────────────────────────────────

    public async Task<ServiceResult<TenantResponse>> GetTenantByIdAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);

        if (tenant == null)
            return ServiceResult<TenantResponse>.Fail("Tenant not found");

        return ServiceResult<TenantResponse>.Ok(ToResponse(tenant));
    }

    // ── Update Tenant ─────────────────────────────────────────

    public async Task<ServiceResult<TenantResponse>> UpdateTenantAsync(
        Guid tenantId, CreateTenantRequest request)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);

        if (tenant == null)
            return ServiceResult<TenantResponse>.Fail("Tenant not found");

        tenant.Name = request.Name.Trim();
        tenant.PlanType = request.PlanType;
        tenant.MaxWabas = request.MaxWabas;
        tenant.MaxUsersPerWaba = request.MaxUsersPerWaba;
        tenant.MaxMessagesPerMonth = request.MaxMessagesPerMonth;
        tenant.IsWhiteLabel = request.IsWhiteLabel;
        tenant.BrandName = request.BrandName;
        tenant.BrandPrimaryColor = request.BrandPrimaryColor;
        tenant.BillingEmail = request.BillingEmail;
        tenant.Country = request.Country;
        tenant.Timezone = request.Timezone ?? "UTC";
        tenant.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.CustomDomain))
            tenant.CustomDomain = request.CustomDomain.ToLower().Trim();

        await _db.SaveChangesAsync();

        return ServiceResult<TenantResponse>.Ok(ToResponse(tenant));
    }

    // ── Deactivate Tenant ─────────────────────────────────────

    public async Task<ServiceResult<bool>> DeactivateTenantAsync(Guid tenantId)
    {
        var tenant = await _db.Tenants.FindAsync(tenantId);

        if (tenant == null)
            return ServiceResult<bool>.Fail("Tenant not found");

        tenant.IsActive = false;
        tenant.DeletedAt = DateTime.UtcNow;
        tenant.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return ServiceResult<bool>.Ok(true);
    }

    // ── Helpers ───────────────────────────────────────────────

    private static string GenerateSlug(string input)
    {
        var slug = input.ToLower().Trim();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", ""); // remove special chars
        slug = Regex.Replace(slug, @"\s+", "-");          // spaces to hyphens
        slug = Regex.Replace(slug, @"-+", "-");           // collapse multiple hyphens
        slug = slug.Trim('-');                             // trim leading/trailing hyphens
        return slug.Length > 100 ? slug[..100] : slug;    // max 100 chars
    }

    private static TenantResponse ToResponse(Tenant t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Slug = t.Slug,
        PlanType = t.PlanType.ToString(),
        IsActive = t.IsActive,
        IsWhiteLabel = t.IsWhiteLabel,
        MaxWabas = t.MaxWabas,
        MaxMessagesPerMonth = t.MaxMessagesPerMonth,
        CustomDomain = t.CustomDomain,
        BillingEmail = t.BillingEmail,
        CreatedAt = t.CreatedAt,
    };
}