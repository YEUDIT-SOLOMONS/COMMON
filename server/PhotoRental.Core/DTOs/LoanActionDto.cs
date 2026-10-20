namespace PhotoRental.Core.DTOs;

// מה שמוצג במשכירה ב"חלון הסימון" שנפתח מהקישור במייל (בלי התחברות, מוגן בטוקן סודי).
// לא כולל את שם השוכרת או פרטי הקשר שלה.
public class LoanActionDto
{
    public string LoanId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public bool IsLocation { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string? StartTime { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public string DeliveryMethod { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;

    // סכום הציוד (בלי משלוח) - הסכום שמשולם במזומן
    public decimal Amount { get; set; }
    public DateTime? PickedUpAt { get; set; }
    public string? PickedUpBy { get; set; }
    // מצב ההחזרה (לחלון "האם הפריט הוחזר?")
    public string Status { get; set; } = string.Empty;
    public DateTime? ActualReturnDate { get; set; }
    public DateTime? NotReturnedReportedAt { get; set; }
    public decimal DailyPrice { get; set; }

    public string? ExtensionStatus { get; set; }
    public int ExtensionDays { get; set; }
    public DateTime? ExtensionNewReturn { get; set; }

    public bool HasPaymentProof { get; set; }
    public string? PaymentProofFileName { get; set; }
}
