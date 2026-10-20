namespace PhotoRental.Core.Interfaces;

// שליחת אימיילים אמיתיים - כרגע דרך Resend (ראו ResendEmailService), עם נפילה חזרה שקטה
// (NullEmailService) כאשר לא הוגדרו פרטי התחברות ל-Resend ב-appsettings.json.
// קובץ מצורף למייל (תוכן בקידוד base64)
public record EmailAttachment(string FileName, string ContentBase64);

public interface IEmailService
{
    // true כאשר יש חיבור אמיתי מוגדר בפועל (ולכן שווה לסמוך על כך שהמייל אכן יישלח)
    bool IsEnabled { get; }

    // replyTo: כתובת לתשובות במקום ברירת המחדל של האתר (למשל פנייה מטופס "צור קשר" - התשובה חוזרת לפונה)
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, string? replyTo);

    // headers: כותרות נוספות (למשל References לשרשור מיילים)
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, string? replyTo, IReadOnlyDictionary<string, string>? headers) =>
        SendAsync(toEmail, toName, subject, htmlBody, attachments, replyTo);

    // שליחה עם קבצים מצורפים בלבד
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments) =>
        SendAsync(toEmail, toName, subject, htmlBody, attachments, null);

    // שליחה בלי קבצים מצורפים
    Task SendAsync(string toEmail, string toName, string subject, string htmlBody) =>
        SendAsync(toEmail, toName, subject, htmlBody, null, null);
}
