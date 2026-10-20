using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// חלון "העלאת אסמכתא" שנפתח מהקישור במייל הדוח החודשי: בלי התחברות, מוגן בטוקן סודי ייחודי לדוח (?t=...).
// טוקן שגוי = 404.
[ApiController]
[Route("api/commission-proof")]
[AllowAnonymous]
public class CommissionProofController : ControllerBase
{
    private readonly ICommissionService _commissionService;

    public CommissionProofController(ICommissionService commissionService)
    {
        _commissionService = commissionService;
    }

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery(Name = "o")] string ownerId, [FromQuery(Name = "m")] string month, [FromQuery(Name = "t")] string? token)
    {
        var view = await _commissionService.GetProofViewAsync(ownerId, month, token);
        return view is null ? NotFound(new { Message = "הקישור אינו תקף" }) : Ok(view);
    }

    [HttpPost]
    public async Task<IActionResult> Submit(
        [FromQuery(Name = "o")] string ownerId, [FromQuery(Name = "m")] string month, [FromQuery(Name = "t")] string? token,
        [FromBody] CommissionProofUploadDto dto)
    {
        try
        {
            var ok = await _commissionService.SubmitProofAsync(ownerId, month, token, dto);
            return ok ? NoContent() : NotFound(new { Message = "הקישור אינו תקף" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}
