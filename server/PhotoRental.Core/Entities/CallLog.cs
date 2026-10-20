namespace PhotoRental.Core.Entities;

// יומן שיחות הטלפון האוטומטיות היוצאות מהאתר - לצפייה במסך הניהול
public class CallLog
{
    public string? Id { get; set; }

    // Availability (אימות זמינות למשכירה) / Reminder (תזכורת החזרה לשוכרת) / NotReturned (הפריט לא הוחזר, לשוכרת)
    public string Kind { get; set; } = string.Empty;

    public string? LoanId { get; set; }
    public string? ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;

    // למי השיחה (צילום מצב בזמן השיחה)
    public string? ToUserId { get; set; }
    public string ToName { get; set; } = string.Empty;
    public string ToPhone { get; set; } = string.Empty;

    // באימות זמינות: המייל של מי שביקשה (המתעניינת)
    public string? InterestedEmail { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    // Scheduled (נדחתה לזמן מותר) / Placed (נשלחה לחיוג) / Answered / NoAnswer / Busy / Failed / Canceled
    public string Status { get; set; } = string.Empty;

    // כשהשיחה נדחתה (שבת/חג/לילה): מתי תצא (שעון ישראל)
    public DateTime? ScheduledForLocal { get; set; }
    public DateTime? PlacedAtUtc { get; set; }

    // פרטים: למשל "לחצה 1 (הפריט פנוי)" או הודעת שגיאה
    public string? Detail { get; set; }

    // ההודעה שהוקראה (לשיחות תזכורת/לא הוחזר)
    public string? Message { get; set; }

    // מזהה השיחה אצל Twilio
    public string? TwilioSid { get; set; }
}
