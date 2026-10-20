namespace PhotoRental.Core.Interfaces;

// שיחת טלפון אוטומטית חד-כיוונית (הקראת הודעה) - לתזכורת החזרה לשוכרת שעתיים לפני סיום ההשכרה.
public class ReminderCallResult
{
    public bool Placed { get; set; }
    public string? Sid { get; set; }
    public string? Error { get; set; }
}

public interface IReminderCallService
{
    // true כשמוגדר חיבור אמיתי (Twilio) ולכן שווה לנסות לחייג
    bool IsEnabled { get; }

    // מחייג למספר ומקריא את ההודעה. מחזיר true אם הבקשה לחיוג התקבלה אצל הספק
    // repeat: האם להשמיע את ההודעה פעמיים (ברירת מחדל) או פעם אחת בלבד
    // statusCallbackUrl: כתובת שאליה Twilio מדווח על סיום השיחה (נענתה / לא נענתה / תפוס / נכשלה)
    // gatherActionUrl: אם ניתן - ההודעה מושמעת כתפריט (המאזינה יכולה להקיש ספרה) והספרה נשלחת לכתובת הזו (Webhook)
    Task<ReminderCallResult> CallAsync(string phone, string message, bool repeat = true, string? statusCallbackUrl = null, string? gatherActionUrl = null);

    Task<ReminderCallResult> CallWithTwimlAsync(string phone, string twiml, string? statusCallbackUrl = null);
}
