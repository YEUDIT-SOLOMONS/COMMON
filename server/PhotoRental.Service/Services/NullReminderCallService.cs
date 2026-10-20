namespace PhotoRental.Service.Services;

using PhotoRental.Core.Interfaces;

// ברירת מחדל כשלא הוגדר Twilio: לא מחייגים (אבל גם לא נכשלים) - תזכורת החזרה נשלחת אז רק במייל
public class NullReminderCallService : IReminderCallService
{
    public bool IsEnabled => false;

    public Task<ReminderCallResult> CallAsync(string phone, string message, bool repeat = true, string? statusCallbackUrl = null, string? gatherActionUrl = null) =>
        Task.FromResult(new ReminderCallResult { Error = "שיחות טלפון אינן מוגדרות" });

    public Task<ReminderCallResult> CallWithTwimlAsync(string phone, string twiml, string? statusCallbackUrl = null) =>
        Task.FromResult(new ReminderCallResult { Error = "שיחות טלפון אינן מוגדרות" });
}
