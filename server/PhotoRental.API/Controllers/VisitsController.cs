using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// ספירת גולשים: הדפדפן מדווח פעם אחת בכל ביקור (לשונית חדשה), בלי שום מידע אישי
[ApiController]
[Route("api/visits")]
[AllowAnonymous]
public class VisitsController : ControllerBase
{
    private readonly IVisitService _visits;

    public VisitsController(IVisitService visits)
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
        await _visits.RecordAsync(body?.IsNew ?? false, Request.Headers.UserAgent.ToString());
        return NoContent();
    }
}
