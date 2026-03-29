// ============================================================
//  ChatRelay — WebhookController
//  Handles Meta webhook verification (GET) and events (POST)
//  Must be publicly accessible — no [Authorize]
// ============================================================

using ChatRelay.API.Data;
using ChatRelay.API.Services;
using ChatRelay.API.Webhooks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChatRelay.API.Controllers;

[ApiController]
[Route("api/webhook")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookProcessor _processor;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<WebhookController> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WebhookController(
        IWebhookProcessor processor,
        ApplicationDbContext db,
        ILogger<WebhookController> logger)
    {
        _processor = processor;
        _db        = db;
        _logger    = logger;
    }

    // ── GET /api/webhook ──────────────────────────────────────
    // Meta calls this once when you register the webhook URL.
    // Must respond with hub.challenge to verify ownership.
    // Each WABA has its own WebhookVerifyToken stored in DB.

    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")]         string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")]    string? challenge)
    {
        _logger.LogInformation(
            "Webhook verification: mode={Mode} token={Token}",
            mode, verifyToken);

        if (mode != "subscribe" || string.IsNullOrEmpty(verifyToken))
            return BadRequest("Invalid verification request");

        // Check if verify token matches any WABA in the system
        var wabaExists = await _db.WabaAccounts
            .AnyAsync(w => w.WebhookVerifyToken == verifyToken && w.IsActive);

        if (!wabaExists)
        {
            _logger.LogWarning(
                "Webhook verification failed — token not found: {Token}", verifyToken);
            return Unauthorized("Verification token not recognized");
        }

        _logger.LogInformation("Webhook verified successfully");

        // Return ONLY the challenge value as plain text — Meta requires this
        return Content(challenge ?? string.Empty, "text/plain");
    }

    // ── POST /api/webhook ─────────────────────────────────────
    // All Meta webhook events come here.
    // Must return 200 quickly — process async, don't block.
    // If Meta doesn't get 200 within ~20s it will retry.

    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        // Read raw body
        string body;
        using (var reader = new System.IO.StreamReader(Request.Body))
            body = await reader.ReadToEndAsync();

        if (string.IsNullOrEmpty(body))
        {
            _logger.LogWarning("Empty webhook body received");
            return Ok(); // Return 200 anyway — don't cause Meta retries
        }

        _logger.LogDebug("Webhook received: {Body}", body[..Math.Min(body.Length, 500)]);

        // Deserialize payload
        MetaWebhookPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<MetaWebhookPayload>(body, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize webhook payload");
            return Ok(); // Return 200 — bad payload won't improve with retries
        }

        if (payload == null)
        {
            _logger.LogWarning("Null webhook payload after deserialization");
            return Ok();
        }

        // Process async — fire and forget pattern
        // This ensures we return 200 to Meta immediately
        // while processing continues in background
        _ = Task.Run(async () =>
        {
            try
            {
                await _processor.ProcessAsync(payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Webhook processing failed");
            }
        });

        // Always return 200 immediately
        return Ok();
    }

    // ── POST /api/webhook/test ────────────────────────────────
    // Dev only — simulate a webhook event without Meta
    // Useful for testing status updates locally

    [HttpPost("test")]
    public async Task<IActionResult> Test([FromBody] MetaWebhookPayload payload)
    {
        if (!HttpContext.RequestServices
            .GetRequiredService<IWebHostEnvironment>().IsDevelopment())
            return NotFound();

        await _processor.ProcessAsync(payload);
        return Ok(new { message = "Test webhook processed" });
    }
}
