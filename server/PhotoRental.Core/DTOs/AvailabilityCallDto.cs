namespace PhotoRental.Core.DTOs;

// נשלח מהלקוח כדי להתחיל שיחת אימות זמינות מדומה אל המשכיר (ראו IAvailabilityCallService)
public class AvailabilityCallStartDto
{
    public string ItemId { get; set; } = string.Empty;

    // פרטי ההזמנה שנאמרים למשכירה בשיחה (אופציונלי): תאריכי ההשכרה בתסדיר yyyy-MM-dd וסכום ההשכרה בשקלים
    public string? StartDate { get; set; }
    public string? ReturnDate { get; set; }
    public decimal? TotalPrice { get; set; }

    // שעות ההתחלה וההחזרה (HH:mm) - לקישור "להמשך ההזמנה" שממלא מראש את אותם מועדים
    public string? StartTime { get; set; }
    public string? ReturnTime { get; set; }

    public List<string>? ExtraItemNames { get; set; }

    // כמה יחידות מהפריט מבוקשות
    public int? Quantity { get; set; }

    // נקבעים בשרת בלבד (לא מהלקוח)
    public string? RenterId { get; set; }
    public string? RequestId { get; set; }
}

// חלון התשובה של המשכירה (קישור במייל)
public class AvailabilityAnswerViewDto
{
    public string ItemName { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? ReturnDate { get; set; }
    public string? Answer { get; set; }
    public bool Canceled { get; set; }
    // ok / already / canceled / irrelevant / booked
    public string? Outcome { get; set; }
}

// בקשת אימות שממתינה לתשובה (או נענתה לאחרונה) - לסמל הצף שמוצג לשוכרת בכל דפי האתר
public class RenterWaitingRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? ReturnDate { get; set; }
    public string? StartTime { get; set; }
    public string? ReturnTime { get; set; }
    public int Quantity { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public bool OwnerEmailed { get; set; }
    public string? RetryAtLocal { get; set; }
    public string Notify { get; set; } = "email";
    public string? Answer { get; set; }
    public DateTime? AnsweredAtUtc { get; set; }
}

// מצב בקשה - לשוכרת (קישור "להמשך ההזמנה")
public class AvailabilityRequestStatusDto
{
    public string Status { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? ReturnDate { get; set; }
}

// נשלח מהלקוח כאשר מדמים את לחיצת המשכיר על מקש הטלפון: 1 = הפריט פנוי, 2 = הפריט תפוס.
// זהו חלק מהדימוי בלבד (ראו הערה מפורטת ב-SimulatedAvailabilityCallService) - בחיבור עתידי לספק
// טלפוניה אמיתי (כגון Twilio), הלחיצה תגיע מהמשכיר עצמו בשיחה האמיתית ולא מבקשת API מהדפדפן.
public class AvailabilityCallRespondDto
{
    public int Digit { get; set; }
}

// מצב שיחת אימות הזמינות, כפי שמוצג ללקוח בזמן אמת (הלקוח מבצע Polling על נקודת קצה זו)
public class AvailabilityCallStatusDto
{
    public string CallId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;

    // Ringing / ConfirmedAvailable / ConfirmedUnavailable / NoAnswer
    public string Status { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;

    // true כאשר מדובר בהדמיה (SimulatedAvailabilityCallService) - כדי שהלקוח ידע להציג את כפתורי
    // הדימוי הידניים (1/2) רק במצב הדמיה, ולא כשמתבצעת שיחה טלפונית אמיתית דרך Twilio
    public bool IsSimulated { get; set; }

    // OwnerQuiet: מתי אפשר להתקשר אליה (טקסט לתצוגה)
    public string? RetryAtText { get; set; }
    public bool OwnerEmailed { get; set; }
}
