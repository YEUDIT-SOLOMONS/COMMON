namespace PhotoRental.Core.Options;

// פרטי ההתחברות לשליחת אימיילים אמיתיים דרך Resend (resend.com) - נטענים מ-appsettings.json
// (מקטע "Resend") ב-Program.cs. כל עוד ApiKey ריק, השרת ממשיך להשתמש ב-NullEmailService
// (לא שולח אימיילים בפועל, אך גם לא נכשל) במקום ResendEmailService.
public class ResendOptions
{
    public string ApiKey { get; set; } = string.Empty;

    // כתובת השולח - חייבת להיות מאומתת (verified domain) בחשבון ה-Resend שלכם
    public string FromEmail { get; set; } = string.Empty;

    // כתובת שאליה מגיעות תשובות של מקבלי המיילים (ברירת מחדל: כתובת השולח)
    public string ReplyTo { get; set; } = string.Empty;
    public string FromName { get; set; } = "common";
}
