using ChatRelay.API.Data;
using ChatRelay.API.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChatRelay.API.Controllers
{
    [ApiController]
    [Route("api/groups")]
    public class GroupsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public GroupsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("create")]
        public IActionResult CreateGroup(Group group)
        {
            var tenant = HttpContext.Items["Tenant"] as Tenant;

            group.TenantId = tenant.Id;
            group.CreatedAt = DateTime.UtcNow;

            _context.Groups.Add(group);
            _context.SaveChanges();

            return Ok(group);
        }
        [HttpPost("add-contact")]
        public IActionResult AddContactToGroup(int groupId, int contactId)
        {
            var groupContact = new GroupContact
            {
                GroupId = groupId,
                ContactId = contactId
            };

            _context.GroupContacts.Add(groupContact);
            _context.SaveChanges();

            return Ok();
        }
    }
}