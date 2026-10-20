using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;
using PhotoRental.Service.Services;

namespace PhotoRental.API.Controllers;

// זרימת אימות זמינות טלפונית מול המשכיר (IVR): "התקשרות" למשכיר שמאשר זמינות בלחיצת
// 1 (פנוי) או 2 (תפוס). כברירת מחדל זו הדמיה בלבד הרצה בזיכרון השרת (SimulatedAvailabilityCallService);
// אם הוגדרו פרטי Twilio אמיתיים ב-appsettings.json, מתבצע חיוג טלפוני אמיתי
// (TwilioAvailabilityCallService), ולחיצת המשכיר מגיעה חזרה דרך ה-Webhook למטה.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AvailabilityCallsController : ControllerBase
{
    private readonly IAvailabilityCallService _callService;
    private readonly IItemScheduleService _schedule;
    private readonly TwilioOptions _twilioOptions;
    private readonly ICallLogRepository _callLogs;

    public AvailabilityCallsController(IAvailabilityCallService callService, TwilioOptions twilioOptions, ICallLogRepository callLogs, IItemScheduleService schedule)
    {
        _schedule = schedule;
        _callService = callService;
        _twilioOptions = twilioOptions;
        _callLogs = callLogs;
    }

    // בדיקה (בלי שיחה) אם הפריט כבר אומת כפנוי לאחרונה למועד הזה
    [HttpPost("recent")]
    public async Task<IActionResult> Recent([FromBody] AvailabilityCallStartDto request)
    {
        request.StartTime = ValidTime(request.StartTime);
        request.ReturnTime = ValidTime(request.ReturnTime);
        return Ok(new { Confirmed = await _callService.IsRecentlyConfirmedAsync(request) });
    }

    // התחלת שיחת אימות זמינות מדומה עבור פריט מסוים - מוחזר מזהה שיחה (CallId) לשימוש ב-Polling
    [HttpPost]
    public async Task<IActionResult> Start([FromBody] AvailabilityCallStartDto request)
    {
        if (DateTime.TryParseExact(request.StartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var start) && start.Date < PhotoRental.Core.Helpers.IsraelTime.Now.Date)
            throw new ArgumentException("לא ניתן לבחור תאריך התחלה שכבר עבר");

        request.RenterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        request.RequestId = null;
        request.StartTime = ValidTime(request.StartTime);
        request.ReturnTime = ValidTime(request.ReturnTime);
        var status = await _callService.StartCallAsync(request);
        return Ok(status);
    }

    // בדיקת מצב השיחה הנוכחי - הלקוח מבצע בקשה זו שוב ושוב (Polling) עד לקבלת תוצאה סופית
    [HttpGet("{callId}")]
    public async Task<IActionResult> GetStatus([FromRoute] string callId)
    {
        var status = await _callService.GetStatusAsync(callId);
        return Ok(status);
    }

    // הדמיית לחיצת המשכיר על מקש הטלפון: 1 = הפריט פנוי, 2 = הפריט תפוס
    // זיהוי משיבון אסינכרוני (Twilio AMD): תא קולי = "לא ענתה" מיד
    [AllowAnonymous]
    [HttpPost("twilio/amd")]
    public async Task<IActionResult> TwilioAmd([FromQuery] string callId)
    {
        var form = await Request.ReadFormAsync();
        if (!string.IsNullOrEmpty(_twilioOptions.AuthToken) && !IsValidTwilioSignature(form))
            return Unauthorized();

        var answeredBy = form.TryGetValue("AnsweredBy", out var ab) ? ab.ToString() : string.Empty;
        if (answeredBy.StartsWith("machine", StringComparison.Ordinal) || answeredBy == "fax")
        {
            await _callService.HandleMachineAsync(callId);
            try { await _callService.NotifyRetryResultsAsync(); } catch { }
        }
        return Ok();
    }

    // בקשות שממתינות לתשובה (או נענו לאחרונה) - לסמל הצף באתר
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _callService.GetWaitingForRenterAsync(userId));
    }

    [HttpPost("requests/{requestId}/cancellation")]
    public async Task<IActionResult> CancelRequest([FromRoute] string requestId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await _callService.CancelRequestAsync(requestId, userId);
        return NoContent();
    }

    // מצב בקשה לשוכרת - קישור "להמשך ההזמנה" מהמייל
    [HttpGet("requests/{requestId}")]
    public async Task<IActionResult> GetRequest([FromRoute] string requestId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var status = await _callService.GetRequestForRenterAsync(requestId, userId);
        return status is null ? NotFound(new { Message = "הבקשה לא נמצאה" }) : Ok(status);
    }

    [HttpPost("{callId}/retries")]
    public async Task<IActionResult> RetryLater([FromRoute] string callId, [FromQuery] string? notify)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var retryId = await _callService.ScheduleRetryAsync(callId, userId, notify);
        return Ok(new { RetryId = retryId });
    }

    [HttpDelete("retries/{retryId}")]
    public async Task<IActionResult> CancelRetry([FromRoute] string retryId)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await _callService.CancelRetryAsync(retryId, userId);
        return NoContent();
    }

    [HttpPost("{callId}/responses")]
    public async Task<IActionResult> Respond([FromRoute] string callId, [FromBody] AvailabilityCallRespondDto request)
    {
        var status = await _callService.RespondAsync(callId, request.Digit);
        return Ok(status);
    }

    // Webhook אמיתי - נקרא ע"י שרתי Twilio עצמם (לא מהדפדפן!) ברגע שהמשכיר לוחץ ספרה בשיחה
    // האמיתית (ראו TwilioAvailabilityCallService.BuildGatherTwiml). מוחרג מאימות ה-JWT הרגיל
    // (Controller זה כולו פתוח ממילא) אך מאמת בעצמו שהבקשה אכן הגיעה מ-Twilio ולא מזויפת, לפני
    // שהיא מעדכנת סטטוס השכרה - אחרת כל אחד היה יכול לשלוח POST ולזייף "הפריט פנוי" בלי שיחה בכלל.
    // ה-Webhook נקרא ע"י Twilio (ללא התחברות) - מאומת בחתימת Twilio בלבד
    [AllowAnonymous]
    [HttpPost("twilio/webhook")]
    public async Task<IActionResult> TwilioWebhook([FromQuery] string callId)
    {
        var form = await Request.ReadFormAsync();

        if (!string.IsNullOrEmpty(_twilioOptions.AuthToken) && !IsValidTwilioSignature(form))
        {
            return Unauthorized();
        }

        var digits = form.TryGetValue("Digits", out var digitsValue) ? digitsValue.ToString() : null;

        // 9 = שמיעה חוזרת של ההודעה (בלי לשנות את מצב השיחה)
        if (digits == "9")
        {
            var webhookUrl = $"{_twilioOptions.PublicBaseUrl.TrimEnd('/')}/api/availabilitycalls/twilio/webhook?callId={callId}";
            var repeatXml = TwilioAvailabilityCallService.GetRepeatTwiml(callId, webhookUrl);
            if (repeatXml is not null)
                return Content(repeatXml, "application/xml");
        }

        TwilioAvailabilityCallService.UpdateFromWebhook(callId, digits);
        await RecordPressedDigitAsync(callId, digits);

        try { await _callService.HandlePhoneAnswerAsync(callId, digits); } catch { }

        try { await _callService.NotifyRetryResultsAsync(); } catch { }

        // חובה להחזיר ל-Twilio תשובת TwiML תקינה כדי שהשיחה תסתיים בצורה מסודרת (במקום שגיאה בטלפון)
        var responseXml = PhotoRental.Service.Services.CallTwiml.SayAndHangup(digits switch
        {
            "1" => "תודה! עדכנו את השוכרת שהפריט פנוי. אם היא תשלים את ההזמנה, תקבלי מייל עם כל הפרטים. אם לא תגיע הזמנה, אין צורך לעשות דבר. יום טוב.",
            "2" => "תודה! עדכנו את השוכרת שהפריט אינו פנוי בתאריכים האלה. יום טוב.",
            _ => "תודה, התגובה נקלטה בהצלחה."
        });
        return Content(responseXml, "application/xml");
    }

    // רושם ביומן השיחות מה המשכירה לחצה
    private async Task RecordPressedDigitAsync(string callId, string? digits)
    {
        try
        {
            var logId = TwilioAvailabilityCallService.GetLogId(callId);
            var log = string.IsNullOrEmpty(logId) ? null : await _callLogs.GetByIdAsync(logId);
            if (log is null) return;

            log.Detail = digits switch
            {
                "1" => "לחצה 1 - הפריט פנוי",
                "2" => "לחצה 2 - הפריט תפוס",
                null or "" => "לא הקישה",
                _ => $"לחצה {digits}"
            };
            await _callLogs.UpdateAsync(log);
        }
        catch
        {
            // היומן אינו חיוני לשיחה
        }
    }

    private static string? ValidTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _) ? value : null;

    private bool IsValidTwilioSignature(IFormCollection form) => TwilioSignature.IsValid(Request, form, _twilioOptions);
}
