using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;
using System;

namespace ChatRelay.API.Services;

public class AuthService : IAuthService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    public AuthService(ApplicationDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<AuthResult> LoginAsync(CRLoginRequest request)
    {
        // Load user with their tenant in one query
        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower().Trim());

        // Fail silently — don't reveal whether email exists
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return Fail("Invalid email or password");

        if (!user.IsActive)
            return Fail("Your account has been deactivated");

        if (!user.EmailVerified)
            return Fail("Please verify your email before logging in");

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
            return Fail($"Account locked. Try again after {user.LockedUntil:HH:mm} UTC");

        if (!user.Tenant.IsActive)
            return Fail("Your account has been suspended. Contact support");

        // Reset failed attempts on success
        user.FailedLoginAttempts = 0;
        user.LastLoginAt = DateTime.UtcNow;
        user.LastLoginIp = ""; // inject IHttpContextAccessor if needed

        // Generate tokens
        var accessToken = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();

        user.RefreshToken = refreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(
            _config.GetValue<int>("Jwt:RefreshTokenExpiryDays", 30));

        await _db.SaveChangesAsync();

        return new AuthResult
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiry = DateTime.UtcNow.AddMinutes(
                _config.GetValue<int>("Jwt:AccessTokenExpiryMinutes", 60)),
            RefreshTokenExpiry = user.RefreshTokenExpiry,
            User = new UserInfo
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString(),
                TenantId = user.TenantId,
                TenantName = user.Tenant.Name
            }
        };
    }

    public async Task<AuthResult> RefreshTokenAsync(string refreshToken)
    {
        var user = await _db.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u =>
                u.RefreshToken == refreshToken &&
                u.RefreshTokenExpiry > DateTime.UtcNow &&
                u.IsActive);

        if (user == null)
            return Fail("Invalid or expired refresh token");

        var newAccessToken = GenerateAccessToken(user);
        var newRefreshToken = GenerateRefreshToken();

        // Rotate refresh token on every use (prevents replay attacks)
        user.RefreshToken = newRefreshToken;
        user.RefreshTokenExpiry = DateTime.UtcNow.AddDays(
            _config.GetValue<int>("Jwt:RefreshTokenExpiryDays", 30));

        await _db.SaveChangesAsync();

        return new AuthResult
        {
            Success = true,
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            AccessTokenExpiry = DateTime.UtcNow.AddMinutes(
                _config.GetValue<int>("Jwt:AccessTokenExpiryMinutes", 60)),
            RefreshTokenExpiry = user.RefreshTokenExpiry,
            User = new UserInfo
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role.ToString(),
                TenantId = user.TenantId,
                TenantName = user.Tenant.Name
            }
        };
    }

    public async Task LogoutAsync(Guid userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return;

        // Invalidate refresh token immediately
        user.RefreshToken = null;
        user.RefreshTokenExpiry = null;
        await _db.SaveChangesAsync();
    }

    public async Task<AuthResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return Fail("User not found");

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            return Fail("Current password is incorrect");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);
        user.RefreshToken = null;  // force re-login on all devices
        user.RefreshTokenExpiry = null;
        await _db.SaveChangesAsync();

        return new AuthResult { Success = true };
    }

    // ── Private helpers ──────────────────────────────────────

    private string GenerateAccessToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new Claim("UserId",   user.Id.ToString()),
            new Claim("TenantId", user.TenantId.ToString()),
            new Claim("FullName", user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                                    _config.GetValue<int>("Jwt:AccessTokenExpiryMinutes", 60)),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GenerateRefreshToken()
    {
        // Cryptographically secure random 64-byte token
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private static AuthResult Fail(string error) =>
        new() { Success = false, Error = error };
}