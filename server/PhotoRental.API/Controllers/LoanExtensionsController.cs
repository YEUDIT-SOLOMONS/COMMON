using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;
using PhotoRental.Service.Services;

namespace PhotoRental.API.Controllers;

// Webhooks של Twilio לתיאום הארכה: השוכרת מקישה 1 בשיחת התזכורת (renter), והמשכירה מאשרת/דוחה בשיחת האישור (owner).
// נקראים ע"י Twilio (ללא התחברות) - מאומתים בחתימת Twilio.
[ApiController]
[Route("api/loan-extensions")]
public class LoanExtensionsController : ControllerBase
{
    private readonly ILoanExtensionService _extensions;
    private readonly ICallLogService _logs;
    private readonly TwilioOptions _twilio;

    public LoanExtensionsController(ILoanExtensionService extensions, ICallLogService logs, TwilioOptions twilio)
    {
        _extensions = extensions;
        _logs = logs;
        _twilio = twilio;
    }

    [AllowAnonymous]
    [HttpPost("twilio/renter")]
    public async Task<IActionResult> Renter([FromQuery] string loanId, [FromQuery] string? logId)
    {
        var form = await Request.ReadFormAsync();
        if (!string.IsNullOrEmpty(_twilio.AuthToken) && !TwilioSignature.IsValid(Request, form, _twilio))
            return Unauthorized();

        var digits = form.TryGetValue("Digits", out var d) ? d.ToString() : null;
        if (digits != "1")
            return Xml(CallTwiml.SayAndHangup("תודה, להתראות."));

        await SetDetailAsync(logId, "ביקשה הארכה (הקישה 1)");
        var message = await _extensions.RequestFromRenterAsync(loanId);
        return Xml(CallTwiml.SayAndHangup(message));
    }

    [AllowAnonymous]
    [HttpPost("twilio/owner")]
    public async Task<IActionResult> Owner([FromQuery] string loanId, [FromQuery] string? logId)
    {
        var form = await Request.ReadFormAsync();
        if (!string.IsNullOrEmpty(_twilio.AuthToken) && !TwilioSignature.IsValid(Request, form, _twilio))
            return Unauthorized();

        var digits = form.TryGetValue("Digits", out var d) ? d.ToString() : null;

        // 9 = שמיעה חוזרת של הבקשה
        if (digits == "9")
        {
            var again = await _extensions.GetOwnerMessageAsync(loanId);
            if (again is not null)
            {
                var url = $"{_twilio.PublicBaseUrl.TrimEnd('/')}/api/loan-extensions/twilio/owner?loanId={loanId}&logId={logId}";
                return Xml(CallTwiml.Gather(again, url));
            }
        }

        await SetDetailAsync(logId, digits switch
        {
            "1" => "אישרה את ההארכה (הקישה 1)",
            "2" => "דחתה את ההארכה (הקישה 2)",
            null or "" => "לא הקישה",
            _ => $"הקישה {digits}"
        });
        var message = await _extensions.OwnerAnsweredAsync(loanId, digits);
        return Xml(CallTwiml.SayAndHangup(message));
    }

    private ContentResult Xml(string twiml) => Content(twiml, "application/xml");

    private Task SetDetailAsync(string? logId, string detail) => _logs.SetDetailAsync(logId, detail);
}
