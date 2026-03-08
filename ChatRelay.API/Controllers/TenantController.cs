using Microsoft.AspNetCore.Mvc;
using ChatRelay.API.Data;
using ChatRelay.API.Models;

namespace ChatRelay.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TenantController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public TenantController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("create")]
        public IActionResult CreateTenant(Tenant tenant)
        {
            tenant.CreatedAt = DateTime.UtcNow;

            _context.Tenants.Add(tenant);
            _context.SaveChanges();

            return Ok(tenant);
        }
    }
}