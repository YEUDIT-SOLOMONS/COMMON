using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using PhotoRental.Core.Interfaces;
using PhotoRental.Service.Services;
using PhotoRental.Core.Options;

namespace PhotoRental.API.Controllers;

// דיווחי סיום שיחה מ-Twilio (נענתה / לא נענתה / תפוס / נכשלה) - מעדכנים את יומן השיחות
[ApiController]
[Route("api/calls")]
public class CallsController : ControllerBase
{
    private readonly ICallLogService _logs;
    private readonly TwilioOptions _twilio;
    private readonly IPartyBridgeService _bridge;
    private readonly ILoanService _loans;

    public CallsController(ICallLogService logs, TwilioOptions twilio, IPartyBridgeService bridge, ILoanService loans)
    {
        _loans = loans;
        _logs = logs;
        _twilio = twilio;
        _bridge = bridge;
    }

    [Authorize]
    [HttpGet("common-number")]
    public IActionResult CommonNumber() => Ok(new { number = _bridge.CommonNumber, callback = _bridge.CallbackEnabled });

    [Authorize]
    [HttpPost("connections")]
    public async Task<IActionResult> Connect([FromBody] ConnectCallRequest request)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId) || string.IsNullOrWhiteSpace(request?.LoanId))
            return BadRequest(new { Message = "בקשה לא תקינה" });

        await _bridge.StartCallbackAsync(userId, request.LoanId);
        return NoContent();
    }

    public sealed record ConnectCallRequest(string LoanId);

    [AllowAnonymous]
    [HttpPost("twilio/inbound")]
    public async Task<IActionResult> Inbound()
    {
        var form = await Request.ReadFormAsync();
        if (!ValidSignature(form))
            return Unauthorized();

        return Xml(await _bridge.HandleIncomingAsync(Field(form, "From")));
    }

    [AllowAnonymous]
    [HttpPost("twilio/inbound/select")]
    public async Task<IActionResult> InboundSelect()
    {
        var form = await Request.ReadFormAsync();
        if (!ValidSignature(form))
            return Unauthorized();

        return Xml(await _bridge.HandleSelectionAsync(Field(form, "From"), Field(form, "Digits")));
    }

    [AllowAnonymous]
    [HttpPost("twilio/inbound/done")]
    public async Task<IActionResult> InboundDone([FromQuery] string logId)
    {
        var form = await Request.ReadFormAsync();
        if (!ValidSignature(form))
            return Unauthorized();

        _ = int.TryParse(Field(form, "DialCallDuration"), out var seconds);
        return Xml(await _bridge.CompleteAsync(logId, Field(form, "DialCallStatus"), seconds));
    }

    // תשובת המשכירה לשיחת "האם הפריט הוחזר?": 1 = הוחזר, 2 = עדיין לא (השוכרת מקבלת תזכורת)
    [AllowAnonymous]
    [HttpPost("twilio/return-check")]
    public async Task<IActionResult> ReturnCheck([FromQuery] string loanId, [FromQuery] string? logId)
    {
        var form = await Request.ReadFormAsync();
        if (!ValidSignature(form))
            return Unauthorized();

        var digits = Field(form, "Digits");
        string message;
        string detail;
        if (digits == "1")
        {
            await _loans.MarkReturnedFromLinkAsync(loanId);
            message = "תודה. סימנו שהפריט הוחזר.";
            detail = "סימנה שהוחזר (הקישה 1)";
        }
        else if (digits == "2")
        {
            var reported = await _loans.ReportNotReturnedAsync(loanId);
            message = reported.CourierHasItem
                ? "תודה. השליח כבר אסף את הפריט מהשוכרת והוא בדרך אלייך. כשיגיע, סמני באתר שהוחזר."
                : reported.Reported
                    ? "תודה. שלחנו תזכורת לשוכרת."
                    : "תודה. השוכרת כבר קיבלה תזכורת, ואנחנו ממשיכים לעקוב.";
            detail = reported.CourierHasItem ? "סימנה שלא הוחזר (השליח כבר אסף)" : "סימנה שלא הוחזר (הקישה 2)";
        }
        else
        {
            message = "לא התקבלה תשובה תקינה. להתראות.";
            detail = string.IsNullOrEmpty(digits) ? "לא הקישה" : $"הקישה {digits}";
        }

        await _logs.SetDetailAsync(logId, detail);
        return Xml(CallTwiml.SayAndHangup(message));
    }

    private bool ValidSignature(IFormCollection form) =>
        string.IsNullOrEmpty(_twilio.AuthToken) || TwilioSignature.IsValid(Request, form, _twilio);

    private static string Field(IFormCollection form, string name) => form.TryGetValue(name, out var v) ? v.ToString() : string.Empty;

    private ContentResult Xml(string twiml) => Content(twiml, "text/xml");

    // נקרא ע"י Twilio (ללא התחברות) - מאומת בחתימת Twilio
    [AllowAnonymous]
    [HttpPost("twilio/status")]
    public async Task<IActionResult> TwilioStatus([FromQuery] string logId)
    {
        var form = await Request.ReadFormAsync();
        if (!string.IsNullOrEmpty(_twilio.AuthToken) && !TwilioSignature.IsValid(Request, form, _twilio))
            return Unauthorized();

        var callStatus = form.TryGetValue("CallStatus", out var cs) ? cs.ToString() : string.Empty;
        await _logs.HandleTwilioStatusAsync(logId, callStatus);
        return Ok();
    }
}
