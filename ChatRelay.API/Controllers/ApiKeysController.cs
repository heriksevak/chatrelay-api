// ============================================================
//  ChatRelay — ApiKeysController
// ============================================================

using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Route("api/apikeys")]
[Authorize]
public class ApiKeysController : TenantBaseController
{
    private readonly IApiKeyService _apiKeyService;

    public ApiKeysController(
        ITenantContext tenantContext,
        IApiKeyService apiKeyService) : base(tenantContext)
    {
        _apiKeyService = apiKeyService;
    }

    // ── POST /api/apikeys ─────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _apiKeyService.CreateAsync(CurrentTenantId, request);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        // Return 201 with full key visible
        return CreatedAtAction(nameof(GetById),
            new { id = result.Data!.Id }, result.Data);
    }

    // ── GET /api/apikeys ──────────────────────────────────────
    [HttpGet]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> GetAll()
    {
        var result = await _apiKeyService.GetAllAsync(
            CurrentTenantId, IsSuperAdmin);
        return Ok(result.Data);
    }

    // ── GET /api/apikeys/{id} ─────────────────────────────────
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var result = await _apiKeyService.GetByIdAsync(
            id, CurrentTenantId, IsSuperAdmin);

        if (!result.Success)
            return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── PUT /api/apikeys/{id} ─────────────────────────────────
    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateApiKeyRequest request)
    {
        var result = await _apiKeyService.UpdateAsync(
            id, CurrentTenantId, IsSuperAdmin, request);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── DELETE /api/apikeys/{id} ──────────────────────────────
    // Revokes the key permanently
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,TenantAdmin")]
    public async Task<IActionResult> Revoke(
        Guid id, [FromBody] RevokeApiKeyRequest? request)
    {
        var result = await _apiKeyService.RevokeAsync(
            id, CurrentTenantId, IsSuperAdmin,
            request ?? new RevokeApiKeyRequest());

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "API key revoked successfully" });
    }

    // ── GET /api/apikeys/scopes ───────────────────────────────
    // Returns list of all available scopes
    [HttpGet("scopes")]
    public IActionResult GetScopes()
    {
        return Ok(new
        {
            scopes = ApiKeyScopes.All.Select(s => new
            {
                scope       = s,
                description = GetScopeDescription(s)
            })
        });
    }

    private static string GetScopeDescription(string scope) => scope switch
    {
        "messages:send"    => "Send messages to contacts",
        "messages:read"    => "Read message history and status",
        "contacts:read"    => "Read contacts list",
        "contacts:write"   => "Create and update contacts",
        "templates:read"   => "Read message templates",
        "templates:write"  => "Create and submit templates",
        "webhooks:read"    => "Read webhook endpoints",
        "webhooks:write"   => "Create and manage webhooks",
        "wabas:read"       => "Read WABA account details",
        "analytics:read"   => "Read analytics and reports",
        _                  => scope
    };
}
