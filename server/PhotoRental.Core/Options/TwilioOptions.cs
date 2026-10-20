namespace PhotoRental.Core.Options;

// הגדרות חיוג יוצא אמיתי דרך Twilio לאימות זמינות טלפוני מול המשכיר - ראו הערה מפורטת
// ב-TwilioAvailabilityCallService. כל עוד AccountSid/AuthToken/FromPhoneNumber אינם מוגדרים,
// השרת ממשיך להשתמש בשירות המדומה (SimulatedAvailabilityCallService).
public class TwilioOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;

    // המספר שממנו מתבצע החיוג בפועל - יכול להיות מספר Twilio רגיל, או מספר אישי/עסקי אמיתי
    // (למשל בישראל, לצורך תאימות לטלפונים "כשרים") שאומת מראש כ"Verified Caller ID" בחשבון
    // ה-Twilio (Console > Phone Numbers > Verified Caller IDs) - Twilio מאפשר להשתמש בכל מספר
    // כזה כ-Caller ID לחיוג יוצא, גם בלי להעביר את המספר עצמו לבעלות Twilio.
    public string FromPhoneNumber { get; set; } = string.Empty;

    // כתובת ה-URL הפומבית (לא localhost) שדרכה Twilio יכול לקרוא בחזרה לשרת שלנו כדי לדווח על
    // הספרה שהמשכיר לחץ (Webhook) - בסביבת פיתוח זו תהיה כתובת ngrok זמנית, ובסביבת ייצור זו
    // כתובת הדומיין האמיתי של השרת. נדרשת גם לבניית ה-URL המדויק שכלפיו מאומתת חתימת Twilio
    // (ראו AvailabilityCallsController.ValidateTwilioSignature).
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string CommonNumber { get; set; } = string.Empty;
}
