// ============================================================
//  ChatRelay — MessagesController
// ============================================================

using ChatRelay.API.Context;
using ChatRelay.API.DTOs;
using ChatRelay.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Route("api/[controller]")]
[Authorize]
public class MessagesController : TenantBaseController
{
    private readonly IMessageService _messageService;

    public MessagesController(
        ITenantContext tenantContext,
        IMessageService messageService) : base(tenantContext)
    {
        _messageService = messageService;
    }

    // ── POST /api/messages ────────────────────────────────────
    // Send a single message of any type
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var wabaId = GetRequiredWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _messageService.SendAsync(
            wabaId.Value, request, CurrentUserId);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return CreatedAtAction(
            nameof(GetById),
            new { id = result.Data!.Id },
            result.Data);
    }

    // ── POST /api/messages/bulk ───────────────────────────────
    // Send same message to multiple contacts (max 1000)
    [HttpPost("bulk")]
    public async Task<IActionResult> SendBulk([FromBody] BulkSendRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var wabaId = GetRequiredWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _messageService.SendBulkAsync(
            wabaId.Value, request, CurrentUserId);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(result.Data);
    }

    // ── GET /api/messages ─────────────────────────────────────
    // List messages with filters
    [HttpGet]
    public async Task<IActionResult> GetMessages(
        [FromQuery] Guid? conversationId,
        [FromQuery] Guid? contactId,
        [FromQuery] string? status,
        [FromQuery] string? direction,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var wabaId = GetRequiredWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        // Cap page size
        pageSize = Math.Min(pageSize, 100);

        var filter = new MessageFilterRequest
        {
            ConversationId = conversationId,
            ContactId      = contactId,
            From           = from,
            To             = to,
            Page           = Math.Max(1, page),
            PageSize       = pageSize,
        };

        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<ChatRelay.Models.MessageStatus>(status, true, out var s))
            filter.Status = s;

        if (!string.IsNullOrEmpty(direction) &&
            Enum.TryParse<ChatRelay.Models.MessageDirection>(direction, true, out var d))
            filter.Direction = d;

        var result = await _messageService.GetMessagesAsync(wabaId.Value, filter);

        return Ok(new
        {
            data       = result.Data,
            page,
            pageSize,
            total      = result.Data?.Count ?? 0
        });
    }

    // ── GET /api/messages/{id} ────────────────────────────────
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var wabaId = GetRequiredWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _messageService.GetByIdAsync(id, wabaId.Value);

        if (!result.Success)
            return NotFoundResult(result.Error!);

        return Ok(result.Data);
    }

    // ── DELETE /api/messages/{id} ─────────────────────────────
    // Cancel a scheduled (pending) message only
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> CancelScheduled(Guid id)
    {
        var wabaId = GetRequiredWabaId();
        if (wabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        await TenantCtx.ValidateWabaAccessAsync(wabaId.Value);

        var result = await _messageService.CancelScheduledAsync(id, wabaId.Value);

        if (!result.Success)
            return BadRequest(new { message = result.Error });

        return Ok(new { message = "Scheduled message cancelled" });
    }

    // ── Helper ────────────────────────────────────────────────

    private Guid? GetRequiredWabaId()
    {
        // Try header first, then route/query
        var header = HttpContext.Request.Headers["X-Waba-Id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(header) && Guid.TryParse(header, out var id))
            return id;
        return CurrentWabaId;
    }
}
