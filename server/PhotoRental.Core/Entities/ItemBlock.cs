namespace PhotoRental.Core.Entities;

// ימים שהמשכירה סימנה שהפריט לא זמין (למשל בשימוש שלה או בתיקון). From/To - תאריכים כוללים
public class ItemBlock
{
    public string? Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
