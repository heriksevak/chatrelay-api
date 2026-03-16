using ChatRelay.API.Data;
using ChatRelay.API.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ChatRelay.API.Controllers
{
    [ApiController]
    [Route("api/messages")]
    public class MessagesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        private readonly WhatsAppService _whatsAppService;

        public MessagesController(ApplicationDbContext context, WhatsAppService whatsAppService)
        {
            _context = context;
            _whatsAppService = whatsAppService;
        }
        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            if (tenant == null)
                return Unauthorized();

            var rawJson = JsonSerializer.Serialize(request);

            var message = new Message
            {
                TenantId = tenant.Id,
                Phone = request.to,
                Type = request.type,
                Content = rawJson,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            try
            {
                var metaResponse = await _whatsAppService.SendMessage(request.to,request.type,rawJson);

                using var doc = JsonDocument.Parse(metaResponse);

                var providerMessageId = doc.RootElement
                    .GetProperty("messages")[0]
                    .GetProperty("id")
                    .GetString();

                message.ProviderMessageId = providerMessageId;
                message.Status = "Sent";

                await _context.SaveChangesAsync();

                return Content(metaResponse, "application/json");
            }
            catch
            {
                message.Status = "ERROR";
                await _context.SaveChangesAsync();

                return StatusCode(500, "Message failed");
            }
        }
    }
}