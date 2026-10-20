using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.Service.Services;

// יומן השיחות האוטומטיות: עדכוני סטטוס מ-Twilio, וההמשך לכל סוג שיחה שלא נענתה
public class CallLogService : ICallLogService
{
    private readonly ICallLogRepository _logs;
    private readonly IAvailabilityCallService _availability;
    private readonly ILoanExtensionService _extensions;
    private readonly IOutboundCallService _outbound;

    public CallLogService(ICallLogRepository logs, IAvailabilityCallService availability, ILoanExtensionService extensions, IOutboundCallService outbound)
    {
        _logs = logs;
        _availability = availability;
        _extensions = extensions;
        _outbound = outbound;
    }

    public async Task SetDetailAsync(string? logId, string detail)
    {
        try
        {
            var log = string.IsNullOrWhiteSpace(logId) ? null : await _logs.GetByIdAsync(logId);
            if (log is null) return;
            log.Detail = detail;
            await _logs.UpdateAsync(log);
        }
        catch
        {
            // היומן אינו חיוני לשיחה
        }
    }

    public async Task<IReadOnlyList<CallLogDto>> GetRecentAsync(int count) =>
        (await _logs.GetAllAsync())
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(count)
            .Select(l => new CallLogDto
            {
                Id = l.Id ?? string.Empty,
                Kind = l.Kind,
                ItemName = l.ItemName,
                ToName = l.ToName,
                ToPhone = l.ToPhone,
                InterestedEmail = l.InterestedEmail,
                CreatedAtUtc = l.CreatedAtUtc,
                Status = l.Status,
                ScheduledForLocal = l.ScheduledForLocal is null ? null : DateTime.SpecifyKind(l.ScheduledForLocal.Value, DateTimeKind.Unspecified),
                Detail = l.Detail,
                Message = l.Message
            })
            .ToList();

    public async Task<CallResendResultDto> ResendWelcomeAsync(string id)
    {
        var original = await _logs.GetByIdAsync(id) ?? throw new KeyNotFoundException("השיחה לא נמצאה");
        if (original.Kind != "Welcome")
            throw new ArgumentException("אפשר לשלוח שוב רק שיחת היכרות");

        static string Digits(string? phone) => new((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        var phoneKey = Digits(original.ToPhone);
        if ((await _logs.GetByStatusAsync("Scheduled")).Any(l => l.Kind == "Welcome" && Digits(l.ToPhone) == phoneKey))
            throw new InvalidOperationException("כבר יש שיחת היכרות מתוזמנת למספר הזה");

        var nowLocal = IsraelTime.Now;
        var next = CallWindow.NextAllowed(nowLocal);
        var created = await _logs.CreateAsync(new CallLog
        {
            Kind = original.Kind,
            ToUserId = original.ToUserId,
            ToName = original.ToName,
            ToPhone = original.ToPhone,
            Message = original.Message,
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Scheduled",
            ScheduledForLocal = next > nowLocal ? next : nowLocal.AddMinutes(-1)
        });
        return new CallResendResultDto { Id = created.Id, ScheduledForLocal = next > nowLocal ? next : null };
    }

    public async Task CancelAsync(string id)
    {
        var log = await _logs.GetByIdAsync(id) ?? throw new KeyNotFoundException("השיחה לא נמצאה");
        if (log.Status is not ("Scheduled" or "RetryScheduled"))
            throw new ArgumentException("אפשר לבטל רק שיחה שעוד לא יצאה");
        log.Status = "Canceled";
        await _logs.UpdateAsync(log);
    }

    public async Task HandleTwilioStatusAsync(string? logId, string callStatus)
    {
        var log = string.IsNullOrWhiteSpace(logId) ? null : await _logs.GetByIdAsync(logId);
        if (log is null || log.Status == "Canceled")
            return;

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
            TwilioAvailabilityCallService.MarkEndedWithoutAnswer(logId!);
            try { await _availability.NotifyRetryResultsAsync(); } catch { }
            try { await _availability.CallEndedWithoutAnswerAsync(logId!); } catch { }
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
    }
}
