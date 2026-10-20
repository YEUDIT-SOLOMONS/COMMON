using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// ספירת גולשים: הדפדפן מדווח פעם אחת בכל ביקור (לשונית חדשה), בלי שום מידע אישי
[ApiController]
[Route("api/visits")]
[AllowAnonymous]
public class VisitsController : ControllerBase
{
    private readonly IVisitRepository _visits;

    public VisitsController(IVisitRepository visits)
    {
        _visits = visits;
    }

    public class VisitBody
    {
        public bool IsNew { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Record([FromBody] VisitBody? body)
    {
        var ua = Request.Headers.UserAgent.ToString().ToLowerInvariant();
        if (ua.Contains("bot") || ua.Contains("crawler") || ua.Contains("spider") || ua.Contains("facebookexternalhit") || ua.Contains("whatsapp"))
            return NoContent();

        try { await _visits.RecordAsync(IsraelTime.Now.ToString("yyyy-MM-dd"), body?.IsNew ?? false); } catch { }
        return NoContent();
    }
}
