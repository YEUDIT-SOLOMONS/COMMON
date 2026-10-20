using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// תשובת המשכירה במייל (פנוי / תפוס) - בלי התחברות, מוגן בטוקן ייחודי לבקשה (?t=...)
[ApiController]
[Route("api/availability-answers")]
[AllowAnonymous]
public class AvailabilityAnswersController : ControllerBase
{
    private readonly IAvailabilityCallService _calls;

    public AvailabilityAnswersController(IAvailabilityCallService calls)
    {
        _calls = calls;
    }

    [HttpGet("{requestId}")]
    public async Task<IActionResult> Get(string requestId, [FromQuery(Name = "t")] string? token)
    {
        var view = await _calls.GetAnswerViewAsync(requestId, token);
        return view is null ? NotFound(new { Message = "הקישור אינו תקף" }) : Ok(view);
    }

    [HttpPost("{requestId}")]
    public async Task<IActionResult> Answer(string requestId, [FromQuery(Name = "t")] string? token, [FromQuery(Name = "a")] int answer)
    {
        if (answer is not (1 or 2)) return BadRequest(new { Message = "תשובה לא תקינה" });
        var view = await _calls.AnswerByEmailAsync(requestId, token, answer == 1);
        return view is null ? NotFound(new { Message = "הקישור אינו תקף" }) : Ok(view);
    }
}
