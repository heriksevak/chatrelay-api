using System.ComponentModel.DataAnnotations;

namespace ChatRelay.API.DTOs;

public class CRLoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string Password { get; set; } = string.Empty;
}

public class CRRefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
    public int? ExpireMinutes { get; set; }

}

public class AuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? AccessTokenExpiry { get; set; }
    public DateTime? RefreshTokenExpiry { get; set; }
    public UserInfo? User { get; set; }
}

public class UserInfo
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string TenantName { get; set; } = string.Empty;
}