// ============================================================
//  ChatRelay — WebhookProcessor AutoReply Integration
//
//  Add IAutoReplyEngine to WebhookProcessor constructor
//  and call it after saving the inbound message.
//
//  Replace your existing WebhookProcessor.cs with these changes:
// ============================================================

// ── 1. Update constructor ─────────────────────────────────────

/*
private readonly IAutoReplyEngine _autoReply;

public WebhookProcessor(
    ApplicationDbContext db,
    IAutoReplyEngine autoReply,        // ADD THIS
    ILogger<WebhookProcessor> logger)
{
    _db        = db;
    _autoReply = autoReply;            // ADD THIS
    _logger    = logger;
}
*/

// ── 2. At the end of ProcessInboundMessageAsync ───────────────
// After: await _db.SaveChangesAsync();
// Add these lines:

/*
    // Fire auto-reply engine (non-blocking — don't fail webhook if reply fails)
    // Only process text-based messages for keyword matching
    var textForMatching = msg.Type.ToLower() switch
    {
        "text"        => msg.Text?.Body,
        "button"      => msg.Button?.Text ?? msg.Button?.Payload,
        "interactive" => msg.Interactive?.ButtonReply?.Title
                         ?? msg.Interactive?.ListReply?.Title,
        _             => null  // media, location etc — no keyword match
    };

    if (!string.IsNullOrWhiteSpace(textForMatching))
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _autoReply.ProcessInboundAsync(
                    wabaId:         waba.Id,
                    contactId:      contact.Id,
                    conversationId: conversation.Id,
                    inboundText:    textForMatching);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AutoReply processing failed for contact {ContactId}", contact.Id);
            }
        });
    }
*/

// ── 3. Program.cs registrations to add ───────────────────────

/*
builder.Services.AddScoped<IAutoReplyEngine, AutoReplyEngine>();
builder.Services.AddScoped<IAutoReplyService, AutoReplyService>();
*/
