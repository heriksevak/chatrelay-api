using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Route("api/[controller]")]
[Authorize]
public class WabaController : TenantBaseController
{
    private readonly IWabaService _wabaService;

    public WabaController(
        ITenantContext tenantContext,
        IWabaService wabaService) : base(tenantContext)
    {
        _wabaService = wabaService;
    }

    // ── POST /api/waba/onboard ────────────────────────────────
    // Primary: Facebook Embedded Signup flow
    // Frontend sends code + wabaId + phoneNumberId + aiSensyApiKey
    [HttpPost("onboard")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin,TenantUser")]
    public async Task<IActionResult> OnboardViaFacebook([FromBody] WabaOnboardRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _wabaService.OnboardViaFacebookAsync(
            request, CurrentTenantId);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(
            nameof(GetWabaById),
            new { id = result.Data!.Id },
            result.Data);
    }

    // ── POST /api/waba/onboard/manual ─────────────────────────
    // Fallback: tenant manually enters all credentials
    [HttpPost("onboard/manual")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin,TenantUser")]
    public async Task<IActionResult> OnboardManual([FromBody] WabaManualRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _wabaService.OnboardManualAsync(request, CurrentTenantId);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(
            nameof(GetWabaById),
            new { id = result.Data!.Id },
            result.Data);
    }

    // ── GET /api/waba ─────────────────────────────────────────
    // Returns WABAs belonging to current tenant
    [HttpGet]
    public async Task<IActionResult> GetWabas()
    {
        var result = await _wabaService.GetWabasAsync(CurrentTenantId, IsSuperAdmin);

        return Ok(result.Data);
    }

    // ── GET /api/waba/{id} ────────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetWabaById(Guid id)
    {
        var result = await _wabaService.GetWabaByIdAsync(id, CurrentTenantId, IsSuperAdmin);

        if (!result.Success)
            return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── PUT /api/waba/{id} ────────────────────────────────────
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin,TenantUser")]
    public async Task<IActionResult> UpdateWaba(
        Guid id, [FromBody] UpdateWabaRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _wabaService.UpdateWabaAsync(id, CurrentTenantId, IsSuperAdmin, request);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── POST /api/waba/{id}/validate ──────────────────────────
    // Re-validates stored credentials on demand
    [HttpPost("{id:guid}/validate")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> ValidateCredentials(Guid id)
    {
        var result = await _wabaService.ValidateCredentialsAsync(id, CurrentTenantId, IsSuperAdmin);

        if (!result.Success)
            return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── DELETE /api/waba/{id} ─────────────────────────────────
    // Disconnects and soft-deletes the WABA
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> DisconnectWaba(Guid id)
    {
        var result = await _wabaService.DisconnectWabaAsync(id, CurrentTenantId, IsSuperAdmin);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "WABA disconnected successfully" });
    }
}
