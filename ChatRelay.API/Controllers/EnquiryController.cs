using ChatRelay.API.Data;
using ChatRelay.Models;
using Microsoft.AspNetCore.Mvc;
using System;

[ApiController]
[Route("api/[controller]")]
public class EnquiryController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public EnquiryController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Enquiry enquiry)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Capture client IP
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Handle proxy (important for deployment)
            if (Request.Headers.ContainsKey("X-Forwarded-For"))
            {
                ipAddress = Request.Headers["X-Forwarded-For"].FirstOrDefault();
            }

            enquiry.IpAddress = ipAddress;

            _context.Enquiries.Add(enquiry);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Enquiry saved successfully" });
        }
        catch (Exception)
        {
            return StatusCode(500, "Something went wrong");
        }
    }
}