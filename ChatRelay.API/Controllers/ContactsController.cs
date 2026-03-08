using ChatRelay.API.Data;
using ChatRelay.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers
{
    [ApiController]
    [Route("api/contacts")]
    public class ContactsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ContactsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("create")]
        public IActionResult Create(Contact contact)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            contact.TenantId = tenant.Id;
            contact.CreatedAt = DateTime.UtcNow;

            _context.Contacts.Add(contact);
            _context.SaveChanges();

            return Ok(contact);
        }

        [HttpGet("list")]
        public IActionResult List()
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            var contacts = _context.Contacts
                .Where(c => c.TenantId == tenant.Id)
                .ToList();

            return Ok(contacts);
        }
    }
}