// ============================================================
//  ChatRelay — TenantBaseController
//  All protected controllers inherit this.
//  Gives every controller clean access to tenant context.
// ============================================================

using ChatRelay.API.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers;

[Authorize]
[ApiController]
public abstract class TenantBaseController : ControllerBase
{
    protected readonly ITenantContext TenantCtx;

    protected TenantBaseController(ITenantContext tenantContext)
    {
        TenantCtx = tenantContext;
    }

    // Shorthand helpers so controllers don't repeat themselves
    protected Guid CurrentTenantId => TenantCtx.TenantId;
    protected Guid CurrentUserId => TenantCtx.UserId;
    protected Guid? CurrentWabaId => TenantCtx.WabaId;
    protected bool IsSuperAdmin => TenantCtx.IsSuperAdmin;

    protected IActionResult Forbidden(string message) =>
        StatusCode(403, new { success = false, message });

    protected IActionResult NotFoundResult(string message) =>
        NotFound(new { success = false, message });
}


// ============================================================
//  USAGE EXAMPLE — MessagesController
//  Shows how any controller uses TenantContext cleanly
// ============================================================

/*

[Route("api/[controller]")]
public class MessagesController : TenantBaseController
{
    private readonly AppDbContext _db;
    private readonly IMessageService _messageService;

    public MessagesController(
        ITenantContext tenantContext,
        AppDbContext db,
        IMessageService messageService) : base(tenantContext)
    {
        _db = db;
        _messageService = messageService;
    }

    // GET /api/messages
    // Returns ONLY messages belonging to current tenant's WABA
    [HttpGet]
    public async Task<IActionResult> GetMessages()
    {
        // WabaId comes from X-Waba-Id header
        if (CurrentWabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        // Validate this WABA belongs to the current tenant
        await TenantCtx.ValidateWabaAccessAsync(CurrentWabaId.Value);

        // Query is already safe — WabaId ensures tenant isolation
        var messages = await _db.Messages
            .Where(m => m.WabaId == CurrentWabaId.Value)
            .OrderByDescending(m => m.CreatedAt)
            .Take(50)
            .ToListAsync();

        return Ok(messages);
    }

    // POST /api/messages
    [HttpPost]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        if (CurrentWabaId == null)
            return BadRequest(new { message = "X-Waba-Id header is required" });

        // One line — validates WABA belongs to tenant, throws 403 if not
        await TenantCtx.ValidateWabaAccessAsync(CurrentWabaId.Value);

        var result = await _messageService.CreateMessageAsync(
            wabaId:  CurrentWabaId.Value,
            request: request,
            sentBy:  CurrentUserId);

        return CreatedAtAction(nameof(GetMessages), new { id = result.Id }, result);
    }
}

*/


// ============================================================
//  USAGE EXAMPLE — WabaController
//  Shows SuperAdmin vs TenantAdmin access difference
// ============================================================

/*

[Route("api/[controller]")]
public class WabaController : TenantBaseController
{
    private readonly AppDbContext _db;

    public WabaController(ITenantContext tenantContext, AppDbContext db)
        : base(tenantContext)
    {
        _db = db;
    }

    // GET /api/waba
    // Tenant sees only their WABAs — SuperAdmin sees all
    [HttpGet]
    public async Task<IActionResult> GetWabas()
    {
        var query = _db.WabaAccounts.AsQueryable();

        // SuperAdmin can see all WABAs across all tenants
        if (!IsSuperAdmin)
            query = query.Where(w => w.TenantId == CurrentTenantId);

        var wabas = await query
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();

        return Ok(wabas);
    }

    // DELETE /api/waba/{id}
    // Only SuperAdmin or the owning TenantAdmin can delete
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteWaba(Guid id)
    {
        // This throws 403 automatically if WABA belongs to different tenant
        await TenantCtx.ValidateWabaAccessAsync(id);

        var waba = await _db.WabaAccounts.FindAsync(id);
        if (waba == null) return NotFoundResult("WABA not found");

        waba.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { message = "WABA deleted successfully" });
    }
}

*/