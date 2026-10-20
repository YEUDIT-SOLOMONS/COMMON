namespace PhotoRental.Service.Services;

using PhotoRental.Core.Interfaces;

// מימוש "ריק" של שירות האימיילים - לשימוש כל עוד לא הוגדר חיבור Resend אמיתי ב-appsettings.json.
// לא שולח כלום ולא זורק שגיאה, כדי שתכונות שתלויות באימייל (איפוס סיסמה, התראות תשלום, תזכורות
// החזרה) ימשיכו לפעול כרגיל בלי אימייל בפועל, באותו העיקרון של שאר השירותים המדומים באתר.
public class NullEmailService : IEmailService
{
    public bool IsEnabled => false;

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, string? replyTo) => Task.CompletedTask;
}
