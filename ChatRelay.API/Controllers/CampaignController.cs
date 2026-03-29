//using ChatRelay.API.Data;
//using ChatRelay.API.Models;
//using Microsoft.AspNetCore.Mvc;

//namespace ChatRelay.API.Controllers
//{
//    [ApiController]
//    [Route("api/campaigns")]
//    public class CampaignController : ControllerBase
//    {
//        private readonly ApplicationDbContext _context;

//        public CampaignController(ApplicationDbContext context)
//        {
//            _context = context;
//        }

//        [HttpPost("create")]
//        public IActionResult CreateCampaign(Campaign campaign)
//        {
//            var tenant = HttpContext.Items["Tenant"] as Tenant;

//            campaign.TenantId = tenant.Id;
//            campaign.Status = "Draft";
//            campaign.CreatedAt = DateTime.UtcNow;

//            _context.Campaigns.Add(campaign);
//            _context.SaveChanges();

//            return Ok(campaign);
//        }
//        [HttpPost("send/{campaignId}")]
//        public IActionResult SendCampaign(int campaignId)
//        {
//            var tenant = HttpContext.Items["Tenant"] as Tenant;

//            var campaign = _context.Campaigns
//                .FirstOrDefault(c => c.Id == campaignId && c.TenantId == tenant.Id);

//            if (campaign == null)
//                return NotFound("Campaign not found");

//            var template = _context.Templates
//                .FirstOrDefault(t => t.Id == campaign.TemplateId && t.TenantId == tenant.Id);

//            if (template == null)
//                return BadRequest("Template not found");

//            var contacts = _context.Contacts
//                .Where(c => c.TenantId == tenant.Id)
//                .ToList();

//            foreach (var contact in contacts)
//            {
//                var message = new Message
//                {
//                    TenantId = tenant.Id,
//                    Phone = contact.Phone,
//                    Content = template.Content,
//                    Status = "Pending",
//                    CreatedAt = DateTime.UtcNow
//                };

//                _context.Messages.Add(message);
//            }

//            campaign.Status = "Running";

//            _context.SaveChanges();

//            return Ok("Campaign started");
//        }

//        [HttpGet("stats/{campaignId}")]
//        public IActionResult GetCampaignStats(int campaignId)
//        {
//            var tenant = HttpContext.Items["Tenant"] as Tenant;

//            var messages = _context.Messages
//                .Where(m => m.TenantId == tenant.Id)
//                .ToList();

//            var total = messages.Count;
//            var sent = messages.Count(m => m.Status == "Sent");
//            var delivered = messages.Count(m => m.Status == "delivered");
//            var read = messages.Count(m => m.Status == "read");
//            var failed = messages.Count(m => m.Status == "failed");

//            return Ok(new
//            {
//                total,
//                sent,
//                delivered,
//                read,
//                failed
//            });
//        }
//    }
//}