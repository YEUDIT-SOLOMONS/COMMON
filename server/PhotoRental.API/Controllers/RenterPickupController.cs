using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// "קיבלת את הפריט?" - קישור במייל לשוכרת, בלי התחברות, מוגן בטוקן ייחודי להשכרה (?t=...)
[ApiController]
[Route("api/renter-pickup")]
[AllowAnonymous]
public class RenterPickupController : ControllerBase
{
    private readonly ILoanService _loanService;

    public RenterPickupController(ILoanService loanService)
    {
        _loanService = loanService;
    }

    [HttpGet("{loanId}")]
    public async Task<IActionResult> Get(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await _loanService.ValidateRenterTokenAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        var view = await _loanService.GetRenterPickupViewAsync(loanId);
        return view is null ? NotFound(new { Message = "ההשכרה לא נמצאה" }) : Ok(view);
    }

    [HttpPost("{loanId}")]
    public async Task<IActionResult> Mark(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await _loanService.ValidateRenterTokenAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        await _loanService.MarkPickedUpAsync(loanId, "Renter");
        return Ok(await _loanService.GetRenterPickupViewAsync(loanId));
    }
}
