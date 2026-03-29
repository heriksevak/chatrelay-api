
using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Route("api/[controller]")]
public class UsersController : TenantBaseController
{
    private readonly IUserService _userService;

    public UsersController(
        ITenantContext tenantContext,
        IUserService userService) : base(tenantContext)
    {
        _userService = userService;
    }

    // ── POST /api/users ───────────────────────────────────────
    // TenantAdmin creates users under their own tenant
    // SuperAdmin can create users under any tenant (pass TenantId in body)
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _userService.CreateUserAsync(
            request: request,
            tenantId: CurrentTenantId,
            createdByRole: Enum.Parse<ChatRelay.Models.UserRole>(TenantCtx.UserRole)
        );

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(
            nameof(GetUserById),
            new { id = result.Data!.Id },
            result.Data);
    }

    // ── GET /api/users ────────────────────────────────────────
    // Returns users scoped to current tenant
    // SuperAdmin sees all users
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> GetUsers()
    {
        var result = await _userService.GetUsersAsync(
            tenantId: CurrentTenantId,
            isSuperAdmin: IsSuperAdmin);

        return Ok(result.Data);
    }

    // ── GET /api/users/{id} ───────────────────────────────────
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> GetUserById(Guid id)
    {
        var result = await _userService.GetUserByIdAsync(
            userId: id,
            tenantId: CurrentTenantId,
            isSuperAdmin: IsSuperAdmin);

        if (!result.Success)
            return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── GET /api/users/me ─────────────────────────────────────
    // Any logged-in user can fetch their own profile
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var result = await _userService.GetUserByIdAsync(
            userId: CurrentUserId,
            tenantId: CurrentTenantId,
            isSuperAdmin: IsSuperAdmin);

        if (!result.Success)
            return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── PUT /api/users/{id} ───────────────────────────────────
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _userService.UpdateUserAsync(
            userId: id,
            tenantId: CurrentTenantId,
            isSuperAdmin: IsSuperAdmin,
            request: request);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── DELETE /api/users/{id} ────────────────────────────────
    // Soft deletes — marks user as inactive
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> DeactivateUser(Guid id)
    {
        // Prevent self-deactivation
        if (id == CurrentUserId)
            return BadRequest(new { message = "You cannot deactivate your own account" });

        var result = await _userService.DeactivateUserAsync(
            userId: id,
            tenantId: CurrentTenantId,
            isSuperAdmin: IsSuperAdmin);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "User deactivated successfully" });
    }
}
