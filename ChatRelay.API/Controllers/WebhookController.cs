using ChatRelay.API.Data;
using ChatRelay.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ChatRelay.API.Controllers
{
    [ApiController]
    [Route("webhooks/meta")]
    public class WebhookController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public WebhookController(ApplicationDbContext context)
        {
            _context = context;
        }
        [HttpGet]
        public IActionResult VerifyWebhook(
            [FromQuery(Name = "hub.mode")] string? mode,
            [FromQuery(Name = "hub.challenge")] string? challenge,
            [FromQuery(Name = "hub.verify_token")] string? verifyToken)
        {
            if (mode == "subscribe" && verifyToken == "chatrelay_verify_token")
            {
                return Ok(challenge);
            }

            return Unauthorized();
        }
        [HttpPost]
        public async Task<IActionResult> ReceiveWebhook([FromBody] JsonElement payload)
        {
            try
            {
                var entry = payload.GetProperty("entry")[0];
                var changes = entry.GetProperty("changes")[0];
                var value = changes.GetProperty("value");

                if (value.TryGetProperty("statuses", out var statuses))
                {
                    var status = statuses[0];

                    var messageId = status.GetProperty("id").GetString();
                    var messageStatus = status.GetProperty("status").GetString();

                    var msg = await _context.Messages
                        .FirstOrDefaultAsync(m => m.ProviderMessageId == messageId);

                    if (msg != null)
                    {
                        msg.Status = messageStatus;

                        if (messageStatus == "delivered")
                            msg.DeliveredAt = DateTime.UtcNow;

                        if (messageStatus == "read")
                            msg.ReadAt = DateTime.UtcNow;

                        await _context.SaveChangesAsync();
                        var log = new WebhookLog
                        {
                            Payload = payload.ToString(),
                            CreatedAt = DateTime.UtcNow
                        };

                        _context.WebhookLog.Add(log);
                        _context.SaveChanges();
                    }
                }
            }
            catch
            {
                // ignore errors for now
            }

            return Ok();
        }
    }
}