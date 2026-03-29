// ============================================================
//  ChatRelay — User & Tenant Registration DTOs
// ============================================================

using ChatRelay.Models;
using System.ComponentModel.DataAnnotations;

namespace ChatRelay.API.DTOs;

// ── Create User (TenantAdmin creates users under their tenant) ─

public class CreateUserRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    // TenantAdmin can only create TenantUser
    // SuperAdmin can create any role
    public UserRole Role { get; set; } = UserRole.TenantUser;

    [MaxLength(20)] public string? PhoneNumber { get; set; }
    [MaxLength(100)] public string? Timezone { get; set; }
    [MaxLength(10)] public string? Language { get; set; }
}

// ── Create Tenant (SuperAdmin only) ───────────────────────────

public class CreateTenantRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    // Slug auto-generated from name if not provided
    [MaxLength(100)]
    public string? Slug { get; set; }

    public PlanType PlanType { get; set; } = PlanType.Free;

    // Plan limits — optional overrides
    public int MaxWabas { get; set; } = 3;
    public int MaxUsersPerWaba { get; set; } = 5;
    public int MaxMessagesPerMonth { get; set; } = 1000;

    // White label
    public bool IsWhiteLabel { get; set; } = false;
    [MaxLength(200)] public string? BrandName { get; set; }
    [MaxLength(7)] public string? BrandPrimaryColor { get; set; }
    [MaxLength(200)] public string? CustomDomain { get; set; }

    // Contact
    [MaxLength(320)] public string? BillingEmail { get; set; }
    [MaxLength(100)] public string? Country { get; set; }
    [MaxLength(50)] public string? Timezone { get; set; }
}

// ── Responses ─────────────────────────────────────────────────

public class UserResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Timezone { get; set; }
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public Guid TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TenantResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsWhiteLabel { get; set; }
    public int MaxWabas { get; set; }
    public int MaxMessagesPerMonth { get; set; }
    public string? CustomDomain { get; set; }
    public string? BillingEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ServiceResult<T>
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public T? Data { get; set; }

    public static ServiceResult<T> Ok(T data) =>
        new() { Success = true, Data = data };

    public static ServiceResult<T> Fail(string error) =>
        new() { Success = false, Error = error };
}