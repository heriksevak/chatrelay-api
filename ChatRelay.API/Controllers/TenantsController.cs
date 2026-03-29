using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Route("api/[controller]")]
[Authorize(Roles = "SuperAdmin")]
public class TenantsController : TenantBaseController
{
    private readonly ITenantService _tenantService;

    public TenantsController(
        ITenantContext tenantContext,
        ITenantService tenantService) : base(tenantContext)
    {
        _tenantService = tenantService;
    }

    // ── POST /api/tenants ─────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _tenantService.CreateTenantAsync(request);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(
            nameof(GetTenantById),
            new { id = result.Data!.Id },
            result.Data);
    }

    // ── GET /api/tenants ──────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAllTenants()
    {
        var result = await _tenantService.GetAllTenantsAsync();
        return Ok(result.Data);
    }

    // ── GET /api/tenants/{id} ─────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetTenantById(Guid id)
    {
        var result = await _tenantService.GetTenantByIdAsync(id);

        if (!result.Success)
            return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── PUT /api/tenants/{id} ─────────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateTenant(
        Guid id, [FromBody] CreateTenantRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _tenantService.UpdateTenantAsync(id, request);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── DELETE /api/tenants/{id} ──────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeactivateTenant(Guid id)
    {
        // Prevent deleting your own tenant
        if (id == CurrentTenantId)
            return BadRequest(new
            {
                message = "You cannot deactivate your own tenant"
            });

        var result = await _tenantService.DeactivateTenantAsync(id);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Tenant deactivated successfully" });
    }
}