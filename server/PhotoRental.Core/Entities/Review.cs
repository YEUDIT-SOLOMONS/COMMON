namespace PhotoRental.Core.Entities;

// דירוג אחרי השכרה שהסתיימה. ByRole: "Renter" - השוכרת מדרגת את הפריט והצלמת (גלוי לכולם),
// "Owner" - המשכירה מדרגת את השוכרת (גלוי להנהלה בלבד). מזהה המסמך: {LoanId}_{ByRole}
public class Review
{
    public string Id => $"{LoanId}_{ByRole}";
    public string LoanId { get; set; } = string.Empty;
    public string ByRole { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string RenterId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Text { get; set; }
    public string? ThankYouNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool Hidden { get; set; }
}
