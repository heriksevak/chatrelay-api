using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Route("api/autoreply")]
[Authorize]
public class AutoReplyController : TenantBaseController
{
    private readonly IAutoReplyService _service;
    private readonly IAutoReplyEngine _engine;

    public AutoReplyController(
        ITenantContext tenantContext,
        IAutoReplyService service,
        IAutoReplyEngine engine) : base(tenantContext)
    {
        _service = service;
        _engine = engine;
    }

    // ── POST /api/autoreply ───────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> CreateRule([FromBody] AutoReplyRuleRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _service.CreateRuleAsync(wabaId.Value, request);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(
            nameof(GetById), new { id = result.Data!.Id }, result.Data);
    }

    // ── GET /api/autoreply ────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetRules()
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _service.GetRulesAsync(wabaId.Value);
        return Ok(result.Data);
    }

    // ── GET /api/autoreply/{id} ───────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _service.GetRuleByIdAsync(id, wabaId.Value);
        if (!result.Success) return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── PUT /api/autoreply/{id} ───────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> UpdateRule(
        Guid id, [FromBody] AutoReplyRuleRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _service.UpdateRuleAsync(id, wabaId.Value, request);
        if (!result.Success) return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── DELETE /api/autoreply/{id} ────────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteRule(Guid id)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _service.DeleteRuleAsync(id, wabaId.Value);
        if (!result.Success) return BadRequest(new { message = result.Error });

        return Ok(new { message = "Rule deleted" });
    }

    // ── PATCH /api/autoreply/{id}/toggle ─────────────────────
    [HttpPatch("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid id, [FromBody] ToggleRequest request)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _service.ToggleRuleAsync(id, wabaId.Value, request.IsActive);
        if (!result.Success) return BadRequest(new { message = result.Error });

        return Ok(new { isActive = request.IsActive });
    }

    // ── PUT /api/autoreply/reorder ────────────────────────────
    // Drag-and-drop priority reorder from dashboard
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<RulePriorityUpdate> updates)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _service.ReorderRulesAsync(wabaId.Value, updates);
        return Ok(new { message = "Rules reordered" });
    }

    // ── POST /api/autoreply/test ──────────────────────────────
    // Test what rule would fire for a given message — no actual send
    [HttpPost("test")]
    public async Task<IActionResult> TestMatch([FromBody] TestRuleRequest request)
    {
        var wabaId = GetWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var (matched, ruleName, preview, reason) =
            await _engine.TestMatchAsync(wabaId.Value, request.InboundMessage);

        return Ok(new TestRuleResponse
        {
            Matched = matched,
            MatchedRuleName = ruleName,
            ReplyPreview = preview,
            NoMatchReason = reason
        });
    }

    private Guid? GetWabaId()
    {
        var header = HttpContext.Request.Headers["X-Waba-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(header) && Guid.TryParse(header, out var id))
            return id;
        return CurrentWabaId;
    }
}

public class ToggleRequest
{
    public bool IsActive { get; set; }
}