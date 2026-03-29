using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Route("api/[controller]")]
[Authorize]
public class TemplatesController : TenantBaseController
{
    private readonly ITemplateService _templateService;

    public TemplatesController(
        ITenantContext tenantContext,
        ITemplateService templateService) : base(tenantContext)
    {
        _templateService = templateService;
    }

    // ── POST /api/templates ───────────────────────────────────
    // Save as draft locally only
    [HttpPost]
    public async Task<IActionResult> CreateLocal([FromBody] CreateTemplateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _templateService.CreateLocalAsync(wabaId.Value, request);
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(nameof(GetById),
            new { id = result.Data!.Id }, result.Data);
    }

    // ── POST /api/templates/submit ────────────────────────────
    // Create + submit to Meta in one step
    [HttpPost("submit")]
    public async Task<IActionResult> CreateAndSubmit(
        [FromBody] CreateTemplateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _templateService.CreateAndSubmitAsync(wabaId.Value, request);
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(nameof(GetById),
            new { id = result.Data!.Id }, result.Data);
    }

    // ── POST /api/templates/{id}/submit ───────────────────────
    // Submit existing draft to Meta
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _templateService.SubmitToMetaAsync(wabaId.Value, id);
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── GET /api/templates ────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] string? status,
        [FromQuery] string? category,
        [FromQuery] string? language,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var filter = new TemplateFilterRequest
        {
            Status = status,
            Category = category,
            Language = language,
            Search = search,
            Page = Math.Max(1, page),
            PageSize = Math.Min(pageSize, 100)
        };

        var result = await _templateService.GetTemplatesAsync(wabaId.Value, filter);
        return Ok(new
        {
            data = result.Data,
            page,
            pageSize,
            total = result.Data?.Count ?? 0
        });
    }

    // ── GET /api/templates/{id} ───────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _templateService.GetByIdAsync(id, wabaId.Value);
        if (!result.Success) return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── POST /api/templates/sync ──────────────────────────────
    // Pull all templates from Meta and sync local DB
    [HttpPost("sync")]
    public async Task<IActionResult> Sync()
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _templateService.SyncFromMetaAsync(wabaId.Value);
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── DELETE /api/templates/{id} ────────────────────────────
    // Deletes from Meta + local DB
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _templateService.DeleteAsync(id, wabaId.Value);
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Template deleted successfully" });
    }

    // ── POST /api/templates/preview ───────────────────────────
    // Preview rendered template + Meta payload without saving
    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromBody] CreateTemplateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _templateService.PreviewAsync(request);
        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    private Guid? GetWabaId()
    {
        var header = HttpContext.Request.Headers["X-Waba-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(header) && Guid.TryParse(header, out var id))
            return id;
        return CurrentWabaId;
    }
}