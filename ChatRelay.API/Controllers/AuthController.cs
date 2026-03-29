using ChatRelay.API.Data;
using ChatRelay.API.DTOs;
using ChatRelay.Models;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] CRLoginRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _authService.LoginAsync(request);
        if (!result.Success)
            return Unauthorized(new { message = result.Error });

        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] CRRefreshRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken);
        if (!result.Success)
            return Unauthorized(new { message = result.Error });

        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var userId = Guid.Parse(User.FindFirst("UserId")!.Value);
        await _authService.LogoutAsync(userId);
        return Ok(new { message = "Logged out successfully" });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        var userId = Guid.Parse(User.FindFirst("UserId")!.Value);
        var result = await _authService.ChangePasswordAsync(userId, request);
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Password changed successfully" });
    }
    [HttpGet("hashgen")]
    public IActionResult GenerateHash([FromQuery] string p)
    {
        var hash = BCrypt.Net.BCrypt.HashPassword(p, workFactor: 12);
        return Ok(new { password = p, hash });
    }
}