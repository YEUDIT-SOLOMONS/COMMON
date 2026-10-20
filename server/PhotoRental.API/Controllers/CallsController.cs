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
    private readonly ICallLogRepository _logs;
    private readonly TwilioOptions _twilio;
    private readonly ILoanExtensionService _extensions;
    private readonly IPartyBridgeService _bridge;
    private readonly ILoanService _loans;
    private readonly IOutboundCallService _outbound;

    private readonly IAvailabilityCallService _availability;

    public CallsController(ICallLogRepository logs, TwilioOptions twilio, ILoanExtensionService extensions, IPartyBridgeService bridge, ILoanService loans, IOutboundCallService outbound, IAvailabilityCallService availability)
    {
        _availability = availability;
        _outbound = outbound;
        _loans = loans;
        _logs = logs;
        _twilio = twilio;
        _extensions = extensions;
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

        var log = string.IsNullOrWhiteSpace(logId) ? null : await _logs.GetByIdAsync(logId);
        if (log is not null)
        {
            log.Detail = detail;
            await _logs.UpdateAsync(log);
        }

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

        var log = string.IsNullOrWhiteSpace(logId) ? null : await _logs.GetByIdAsync(logId);
        if (log is null || log.Status == "Canceled")
            return Ok();

        var callStatus = form.TryGetValue("CallStatus", out var cs) ? cs.ToString() : string.Empty;
        log.Status = callStatus switch
        {
            "completed" => "Answered",
            "no-answer" => "NoAnswer",
            "busy" => "Busy",
            "failed" or "canceled" => "Failed",
            _ => log.Status
        };
        await _logs.UpdateAsync(log);

        // שיחת אימות זמינות שנדחתה / הסתיימה בלי הקשה - "לא ענתה" מיד, בלי לחכות לתום זמן ההמתנה
        if (log.Kind == "Availability" && callStatus is "completed" or "no-answer" or "busy" or "failed" or "canceled")
        {
            PhotoRental.Service.Services.TwilioAvailabilityCallService.MarkEndedWithoutAnswer(logId);
            try { await _availability.NotifyRetryResultsAsync(); } catch { }
            try { await _availability.CallEndedWithoutAnswerAsync(logId); } catch { }
        }

        // המשכירה לא ענתה / לא הקישה לבקשת הארכה - ניסיון חוזר אחד בעוד כמה דקות (אם הבקשה עדיין ממתינה)
        if (log.Kind == "ExtensionRequest" && !string.IsNullOrEmpty(log.LoanId) && log.Status is "NoAnswer" or "Busy" or "Failed" or "Answered")
            await _extensions.OwnerCallEndedWithoutAnswerAsync(log.LoanId);

        if (log.Kind is "ExtensionRequest" or "PickupReminder" or "PickupOwnerReminder" && log.Status is "NoAnswer" or "Busy" or "Failed")
        {
            try { await _outbound.SendMissedCallEmailAsync(log); } catch { }
        }

        if (log.Kind == "Welcome" && log.Status is "NoAnswer" or "Busy" or "Failed")
        {
            try { await _outbound.WelcomeUnansweredAsync(log); } catch { }
        }

        // שיחה שלא נענתה - ניסיון חוזר (ראו OutboundCallService.CallEndedUnansweredAsync)
        if (log.Kind is "Reminder" or "NotReturned" or "ExtensionApproved" or "ExtensionDeclined" or "PickupReminder" or "PickupOwnerReminder"
            && log.Status is "NoAnswer" or "Busy" or "Failed")
            await _outbound.CallEndedUnansweredAsync(log);

        return Ok();
    }
}
