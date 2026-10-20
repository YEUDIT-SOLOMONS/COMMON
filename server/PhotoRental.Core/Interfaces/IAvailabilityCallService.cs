namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

// שירות אימות זמינות טלפוני מול המשכיר. המימוש הנוכחי (SimulatedAvailabilityCallService) הוא הדמיה
// בלבד הרצה בזיכרון השרת - כדי לחבר ספק טלפוניה אמיתי (כגון Twilio) יש לממש ממשק זה מחדש עם חיוג
// יוצא אמיתי ו-Webhook שמקבל את לחיצת המשכיר, מבלי לשנות את שאר האפליקציה (Controller/Frontend).
public interface IAvailabilityCallService
{
    Task<AvailabilityCallStatusDto> StartCallAsync(AvailabilityCallStartDto request);
    Task<AvailabilityCallStatusDto> GetStatusAsync(string callId);
    Task<AvailabilityCallStatusDto> RespondAsync(string callId, int digit);

    // שיחה שלא נענתה: ניסיון נוסף בעוד 10 דקות, והתוצאה נשלחת לשוכרת במייל
    Task<string?> ScheduleRetryAsync(string callId, string renterId, string? notify = null) => Task.FromResult<string?>(null);
    Task CancelRetryAsync(string retryId, string renterId) => Task.CompletedTask;
    Task ProcessRetriesAsync() => Task.CompletedTask;
    Task NotifyRetryResultsAsync() => Task.CompletedTask;

    // שיחת אימות שהסתיימה בלי תשובה: ניסיון חוזר אוטומטי בעוד 12 דקות, ואם גם הוא לא נענה - מייל למשכירה 12 דקות אחריו
    Task CallEndedWithoutAnswerAsync(string logId) => Task.CompletedTask;

    Task HandlePhoneAnswerAsync(string callId, string? digits) => Task.CompletedTask;
    Task HandleMachineAsync(string callId) => Task.CompletedTask;
    Task<AvailabilityAnswerViewDto?> GetAnswerViewAsync(string requestId, string? token) => Task.FromResult<AvailabilityAnswerViewDto?>(null);
    Task<AvailabilityAnswerViewDto?> AnswerByEmailAsync(string requestId, string? token, bool available) => Task.FromResult<AvailabilityAnswerViewDto?>(null);
    Task<AvailabilityRequestStatusDto?> GetRequestForRenterAsync(string requestId, string renterId) => Task.FromResult<AvailabilityRequestStatusDto?>(null);
    Task<IReadOnlyList<RenterWaitingRequestDto>> GetWaitingForRenterAsync(string renterId) =>
        Task.FromResult<IReadOnlyList<RenterWaitingRequestDto>>(Array.Empty<RenterWaitingRequestDto>());
    Task CancelRequestAsync(string requestId, string renterId) => Task.CompletedTask;

    // האם המשכירה כבר אישרה לאחרונה שהפריט פנוי במועד הזה (עד 5 שעות לכל צד) - אז לא צריך שיחה
    Task<bool> IsRecentlyConfirmedAsync(AvailabilityCallStartDto request) => Task.FromResult(false);

    // מסך הניהול: שיחה חוזרת ו/או מייל למשכירה על בקשה שעוד לא נענתה
    Task<(DateTime? CallAtLocal, bool Emailed)> AdminResendAsync(string callLogId, bool call, bool email) =>
        Task.FromResult<(DateTime?, bool)>((null, false));
}
