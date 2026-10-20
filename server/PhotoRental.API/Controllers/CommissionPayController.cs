using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// תשלום עמלה באשראי (סאמיט): הקישור שבמייל הדוח מוביל לעמוד באתר (start) שיוצר דף תשלום חדש עם הסכום המדויק,
// ואחרי התשלום סאמיט מחזירה לעמוד "התשלום התקבל" (confirm) שמאמת מול סאמיט וסומן את הדוח כשולם.
// בלי התחברות: start מוגן בטוקן הסודי של הדוח (?t=...), ו-confirm מאומת בבדיקה בין שרת לשרת מול סאמיט.
[ApiController]
[Route("api/commission-pay")]
[AllowAnonymous]
public class CommissionPayController : ControllerBase
{
    private readonly ICommissionService _commissionService;

    public CommissionPayController(ICommissionService commissionService)
    {
        _commissionService = commissionService;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start([FromQuery(Name = "o")] string ownerId, [FromQuery(Name = "m")] string month, [FromQuery(Name = "t")] string? token, [FromQuery] bool consent = false)
    {
        try
        {
            var url = await _commissionService.BeginCardPaymentAsync(ownerId, month, token, consent);
            return url is null ? NotFound(new { Message = "הקישור אינו תקף" }) : Ok(new { RedirectUrl = url });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    [EnableRateLimiting("contact")]
    [HttpPost("email")]
    public async Task<IActionResult> Email([FromQuery(Name = "o")] string ownerId, [FromQuery(Name = "m")] string month, [FromQuery(Name = "t")] string? token)
    {
        var sent = await _commissionService.ResendReportAsync(ownerId, month, token);
        return sent ? Ok(new { Sent = true }) : NotFound(new { Message = "הקישור אינו תקף" });
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] CommissionCardConfirmDto dto)
    {
        return Ok(await _commissionService.ConfirmCardPaymentAsync(dto));
    }
}
