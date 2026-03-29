// ============================================================
//  ChatRelay — IUserService + UserService
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using Microsoft.EntityFrameworkCore;

namespace ChatRelay.API.Services;

public interface IUserService
{
    Task<ServiceResult<UserResponse>> CreateUserAsync(
        CreateUserRequest request,
        Guid tenantId,
        UserRole createdByRole);

    Task<ServiceResult<List<UserResponse>>> GetUsersAsync(
        Guid tenantId,
        bool isSuperAdmin);

    Task<ServiceResult<UserResponse>> GetUserByIdAsync(
        Guid userId,
        Guid tenantId,
        bool isSuperAdmin);

    Task<ServiceResult<UserResponse>> UpdateUserAsync(
        Guid userId,
        Guid tenantId,
        bool isSuperAdmin,
        UpdateUserRequest request);

    Task<ServiceResult<bool>> DeactivateUserAsync(
        Guid userId,
        Guid tenantId,
        bool isSuperAdmin);
}

public class UserService : IUserService
{
    private readonly ApplicationDbContext _db;

    public UserService(ApplicationDbContext db)
    {
        _db = db;
    }

    // ── Create User ───────────────────────────────────────────

    public async Task<ServiceResult<UserResponse>> CreateUserAsync(
        CreateUserRequest request,
        Guid tenantId,
        UserRole createdByRole)
    {
        // TenantAdmin cannot create SuperAdmin or another TenantAdmin
        if (createdByRole == UserRole.TenantAdmin &&
            request.Role != UserRole.TenantUser)
        {
            return ServiceResult<UserResponse>.Fail(
                "TenantAdmin can only create users with the TenantUser role");
        }

        // Email must be unique across the entire platform
        var emailExists = await _db.Users
            .AnyAsync(u => u.Email == request.Email.ToLower().Trim());

        if (emailExists)
            return ServiceResult<UserResponse>.Fail(
                "A user with this email already exists");

        // Validate tenant exists and is active
        var tenant = await _db.Tenants.FindAsync(tenantId);
        if (tenant == null || !tenant.IsActive)
            return ServiceResult<UserResponse>.Fail("Tenant not found or inactive");

        // Check user limit for this tenant
        var currentUserCount = await _db.Users
            .CountAsync(u => u.TenantId == tenantId);

        // Simple per-tenant user cap (MaxUsersPerWaba used as overall cap here)
        if (currentUserCount >= tenant.MaxUsersPerWaba * tenant.MaxWabas)
            return ServiceResult<UserResponse>.Fail(
                "User limit reached for your plan. Please upgrade.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = request.FullName.Trim(),
            Email = request.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12),
            Role = request.Role,
            IsActive = true,
            EmailVerified = true,   // no email verification for now
            PhoneNumber = request.PhoneNumber,
            Timezone = request.Timezone,
            Language = request.Language ?? "en",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return ServiceResult<UserResponse>.Ok(ToResponse(user));
    }

    // ── Get Users ─────────────────────────────────────────────

    public async Task<ServiceResult<List<UserResponse>>> GetUsersAsync(
        Guid tenantId,
        bool isSuperAdmin)
    {
        var query = _db.Users.AsQueryable();

        // SuperAdmin sees all users across all tenants
        // TenantAdmin sees only their tenant's users
        if (!isSuperAdmin)
            query = query.Where(u => u.TenantId == tenantId);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => ToResponse(u))
            .ToListAsync();

        return ServiceResult<List<UserResponse>>.Ok(users);
    }

    // ── Get Single User ───────────────────────────────────────

    public async Task<ServiceResult<UserResponse>> GetUserByIdAsync(
        Guid userId,
        Guid tenantId,
        bool isSuperAdmin)
    {
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
            return ServiceResult<UserResponse>.Fail("User not found");

        // TenantAdmin cannot access users from other tenants
        if (!isSuperAdmin && user.TenantId != tenantId)
            return ServiceResult<UserResponse>.Fail("User not found");

        return ServiceResult<UserResponse>.Ok(ToResponse(user));
    }

    // ── Update User ───────────────────────────────────────────

    public async Task<ServiceResult<UserResponse>> UpdateUserAsync(
        Guid userId,
        Guid tenantId,
        bool isSuperAdmin,
        UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
            return ServiceResult<UserResponse>.Fail("User not found");

        if (!isSuperAdmin && user.TenantId != tenantId)
            return ServiceResult<UserResponse>.Fail("User not found");

        // Only SuperAdmin can change roles
        if (request.Role.HasValue && !isSuperAdmin)
            return ServiceResult<UserResponse>.Fail(
                "Only SuperAdmin can change user roles");

        if (!string.IsNullOrWhiteSpace(request.FullName))
            user.FullName = request.FullName.Trim();

        if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            user.PhoneNumber = request.PhoneNumber;

        if (!string.IsNullOrWhiteSpace(request.Timezone))
            user.Timezone = request.Timezone;

        if (!string.IsNullOrWhiteSpace(request.Language))
            user.Language = request.Language;

        if (request.Role.HasValue && isSuperAdmin)
            user.Role = request.Role.Value;

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ServiceResult<UserResponse>.Ok(ToResponse(user));
    }

    // ── Deactivate User (soft delete) ─────────────────────────

    public async Task<ServiceResult<bool>> DeactivateUserAsync(
        Guid userId,
        Guid tenantId,
        bool isSuperAdmin)
    {
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
            return ServiceResult<bool>.Fail("User not found");

        if (!isSuperAdmin && user.TenantId != tenantId)
            return ServiceResult<bool>.Fail("User not found");

        // Can't deactivate yourself
        // (caller should pass currentUserId separately if needed)
        if (user.Role == UserRole.SuperAdmin && !isSuperAdmin)
            return ServiceResult<bool>.Fail("Cannot deactivate a SuperAdmin");

        user.IsActive = false;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        // Invalidate any active sessions
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;

        await _db.SaveChangesAsync();

        return ServiceResult<bool>.Ok(true);
    }

    // ── Mapper ────────────────────────────────────────────────

    private static UserResponse ToResponse(User u) => new()
    {
        Id = u.Id,
        FullName = u.FullName,
        Email = u.Email,
        Role = u.Role.ToString(),
        PhoneNumber = u.PhoneNumber,
        Timezone = u.Timezone,
        IsActive = u.IsActive,
        EmailVerified = u.EmailVerified,
        TenantId = u.TenantId,
        CreatedAt = u.CreatedAt,
    };
}

// ── UpdateUserRequest ─────────────────────────────────────────

public class UpdateUserRequest
{
    [System.ComponentModel.DataAnnotations.MaxLength(200)]
    public string? FullName { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string? PhoneNumber { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? Timezone { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(10)]
    public string? Language { get; set; }

    // Only used when caller is SuperAdmin
    public ChatRelay.Models.UserRole? Role { get; set; }
}