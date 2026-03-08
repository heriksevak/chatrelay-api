using ChatRelay.API.Data;
using ChatRelay.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers
{
    [ApiController]
    [Route("api/templates")]
    public class TemplatesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TemplatesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("create")]
        public IActionResult CreateTemplate(Template template)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            template.TenantId = tenant.Id;
            template.CreatedAt = DateTime.UtcNow;

            _context.Templates.Add(template);
            _context.SaveChanges();

            return Ok(template);
        }

        [HttpPost("send-template")]
        public IActionResult SendTemplate(int templateId, string phone)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            var template = _context.Templates
                .FirstOrDefault(t => t.Id == templateId && t.TenantId == tenant.Id);

            if (template == null)
                return BadRequest("Template not found");

            var message = new Message
            {
                TenantId = tenant.Id,
                Phone = phone,
                Content = template.Content,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            _context.SaveChanges();

            return Ok(message);
        }
    }
}