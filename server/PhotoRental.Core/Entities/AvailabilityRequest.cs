namespace PhotoRental.Core.Entities;

// בקשת אימות זמינות ששמורה במסד: כך תשובה מאוחרת (במייל, או אחרי שהשרת הופעל מחדש) לא הולכת לאיבוד
public class AvailabilityRequest
{
    public string? Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string RenterId { get; set; } = string.Empty;
    public string? StartDate { get; set; }
    public string? ReturnDate { get; set; }
    public string RequestJson { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;

    // איך לעדכן את השוכרת: "email" / "phone"
    public string Notify { get; set; } = "email";

    // "Available" / "Unavailable" / null
    public string? Answer { get; set; }
    public string? AnsweredVia { get; set; }
    public DateTime? AnsweredAtUtc { get; set; }
    public bool Canceled { get; set; }
    public bool OwnerEmailed { get; set; }

    // ניסיון חוזר מתוזמן (שיחה אחרי 10 דקות / בבוקר): מזהה ביומן השיחות ומתי (שעון ישראל, "yyyy-MM-ddTHH:mm")
    public string? RetryLogId { get; set; }
    public string? RetryAtLocal { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    // תזכורת לשוכרת 40 דקות אחרי אישור הזמינות, אם עוד לא הזמינה
    public DateTime? OrderReminderSentAtUtc { get; set; }
}
