using ChatRelay.API.Data;
using ChatRelay.API.Models;
using Microsoft.AspNetCore.Mvc;

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
        public async Task<IActionResult> SendMessage([FromBody] Message request)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            if (tenant == null)
                return Unauthorized();

            var message = new Message
            {
                TenantId = tenant.Id,
                Phone = request.Phone,
                Content = request.Content,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            try
            {
                var providerMessageId = await _whatsAppService.SendMessage(
                    request.Phone,
                    request.Content
                );

                message.ProviderMessageId = providerMessageId;
                message.Status = "Sent";

                await _context.SaveChangesAsync();
            }
            catch
            {
                message.Status = "Failed";
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                message = "Message processed",
                id = message.Id
            });
        }
    }
}