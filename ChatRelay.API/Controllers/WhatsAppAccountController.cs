using ChatRelay.API.Data;
using ChatRelay.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers
{
    [ApiController]
    [Route("api/whatsapp")]
    public class WhatsAppAccountController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public WhatsAppAccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("connect")]
        public IActionResult ConnectAccount(WhatsAppAccount account)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            account.TenantId = tenant.Id;
            account.CreatedAt = DateTime.UtcNow;

            _context.WhatsAppAccounts.Add(account);
            _context.SaveChanges();

            return Ok(account);
        }
    }
}