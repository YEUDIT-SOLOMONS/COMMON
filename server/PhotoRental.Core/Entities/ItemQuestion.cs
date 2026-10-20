namespace PhotoRental.Core.Entities;

// שאלה של שוכרת למשכירה על פריט, והתכתבות ביניהן (בפיקוח הנהלת האתר)
public class ItemQuestion
{
    public string? Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string RenterId { get; set; } = string.Empty;

    // קישורים אישיים מהמייל (בלי התחברות)
    public string OwnerToken { get; set; } = string.Empty;
    public string RenterToken { get; set; } = string.Empty;

    public List<ItemQuestionMessage> Messages { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastMessageAtUtc { get; set; }

    public DateTime? OwnerReadAtUtc { get; set; }
    public DateTime? RenterReadAtUtc { get; set; }
}

public class ItemQuestionMessage
{
    // "Renter" / "Owner"
    public string From { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }

    // תמונות מצורפות (אוסף QuestionImages)
    public List<string> ImageIds { get; set; } = new();
}
