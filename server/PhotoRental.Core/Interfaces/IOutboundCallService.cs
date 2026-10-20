namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public enum CallKind
{
    Reminder,          // תזכורת החזרה לשוכרת (שעתיים לפני) - עם אפשרות להקיש 1 לבקשת הארכה
    NotReturned,       // "הפריט לא הוחזר" לשוכרת
    ExtensionRequest,  // בקשת אישור הארכה - למשכירה (הקשה: 1 מאשרת, 2 דוחה)
    ExtensionApproved, // עדכון לשוכרת: ההארכה אושרה
    ExtensionDeclined, // עדכון לשוכרת: ההארכה לא אושרה
    ExtensionChargeFailed, // עדכון לשוכרת: ההארכה אושרה אך החיוב מהכרטיס נכשל, ולכן לא אושרה
    ReturnCheck,       // "האם הפריט הוחזר?" - למשכירה, 45 דקות אחרי מועד ההחזרה (הקשה: 1 הוחזר, 2 לא הוחזר)
    Welcome,           // שיחת היכרות לחברה חדשה: לשמור את מספר האתר באנשי הקשר
    PickupReminder,    // לשוכרת, שעה לפני איסוף עצמי
    PickupOwnerReminder, // למשכירה, שעתיים לפני האיסוף העצמי / הגעת השליח
    SaleNotice         // יד 2: הודעה למוכרת שהפריט נמכר
}

// תוצאת בקשת שיחה: יצאה עכשיו, נדחתה לזמן מותר (שבת/חג/לילה), או לא נעשה כלום
public class CallOutcome
{
    public bool Placed { get; set; }
    public DateTime? ScheduledForLocal { get; set; }
}

// שיחות יוצאות לשוכרת (תזכורת החזרה, "הפריט לא הוחזר") עם יומן שיחות ודחייה אוטומטית של שיחות שנופלות
// על שבת או חג - למוצאי השבת/החג.
public interface IOutboundCallService
{
    bool IsEnabled { get; }

    // atLocal: לתזמן שיחה למועד מסוים (שעון ישראל); אחרת מיד, או במוצאי שבת/חג אם עכשיו אסור
    // שיחה שלא נענתה: מייל גיבוי אחד עם אותו תוכן (ולבקשת הארכה - כפתורי אישור/דחייה)
    Task SendMissedCallEmailAsync(CallLog log);

    Task<CallOutcome> CallForLoanAsync(CallKind kind, Loan loan, Item? item = null, User? borrower = null, DateTime? atLocal = null, string? message = null);

    // שיחת היכרות לחברה חדשה (פעם אחת לכל משתמשת), מיד - חוץ משבת/חג, אז במוצאי השבת/החג
    Task<CallOutcome> CallWelcomeAsync(User user);

    // שיחה עם הודעה חופשית (יד 2) - מכבדת שבת/חג ושעות שקטות
    Task<CallOutcome> CallMessageAsync(User user, CallKind kind, string message, string? itemName = null) => Task.FromResult(new CallOutcome());

    // שיחה לשוכרת שלא נענתה - ניסיון חוזר אחרי 10 דקות. תזכורת החזרה: עד 3 שיחות ואחרי השלישית הודעה למייל האתר;
    // "הפריט לא הוחזר" / "הארכה אושרה" / "הארכה נדחתה": עד 2 שיחות. תזכורות איסוף (לשוכרת ולמשכירה): ניסיון חוזר אחד אחרי רבע שעה
    Task CallEndedUnansweredAsync(CallLog log);

    // שיחת היכרות שלא נענתה - ניסיון חוזר אחד כעבור 13 דקות
    Task WelcomeUnansweredAsync(CallLog log);

    // מחייג שיחות שנדחו וזמנן הגיע (נקרא בכל סבב של שירות הרקע)
    Task ProcessScheduledAsync();
}
