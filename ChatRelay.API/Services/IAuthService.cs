using ChatRelay.API.DTOs;

namespace ChatRelay.API.Services;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(CRLoginRequest request);
    Task<AuthResult> RefreshTokenAsync(string refreshToken);
    Task LogoutAsync(Guid userId);
    Task<AuthResult> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);
}