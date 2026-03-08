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
        public IActionResult SendMessage([FromBody] Message request)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            var message = new Message
            {
                TenantId = tenant.Id,
                Phone = request.Phone,
                Content = request.Content,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            _context.SaveChanges();

            return Ok(new { message = "Message queued", id = message.Id });
        }
    }
}