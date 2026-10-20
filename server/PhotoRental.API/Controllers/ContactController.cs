using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ContactController : ControllerBase
{
    private readonly IContactMessageService _contactMessageService;

    public ContactController(IContactMessageService contactMessageService)
    {
        _contactMessageService = contactMessageService;
    }

    // שליחת פנייה מדף "יצירת קשר"
    [EnableRateLimiting("contact")]
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] ContactMessageCreateDto request)
    {
        await _contactMessageService.SendAsync(request);
        return Ok(new { Message = "הפנייה התקבלה בהצלחה" });
    }
}
