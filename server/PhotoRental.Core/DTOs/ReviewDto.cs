namespace PhotoRental.Core.DTOs;

public class ReviewSubmitDto
{
    public int Stars { get; set; }
    public string? Text { get; set; }

    // מילה טובה מהשוכרת למשכירה - נשלחת אליה בלבד, לא מוצגת לציבור
    public string? ThankYouNote { get; set; }
}

public class ThankYouNoteDto
{
    public string Text { get; set; } = string.Empty;
    public string FromFirstName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class PublicReviewDto
{
    public int Stars { get; set; }
    public string? Text { get; set; }
    public string AuthorFirstName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public class RatingSummaryDto
{
    public double Average { get; set; }
    public int Count { get; set; }
}

public class ItemReviewsDto : RatingSummaryDto
{
    public List<PublicReviewDto> Reviews { get; set; } = new();
}

// השכרה שהסתיימה ועוד לא דורגה ע"י המשתמשת. Role: "Renter" (לדרג את הפריט) / "Owner" (לדרג את השוכרת)
public class PendingReviewDto
{
    public string LoanId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public DateTime? ReturnedAt { get; set; }
}

public class AdminReviewDto
{
    public string Id { get; set; } = string.Empty;
    public string LoanId { get; set; } = string.Empty;
    public string ByRole { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public int Stars { get; set; }
    public string? Text { get; set; }
    public string? ThankYouNote { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool Hidden { get; set; }
}
