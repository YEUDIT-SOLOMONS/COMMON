namespace PhotoRental.Core.Entities;

// פרטי ההזמנה שנשמרים בשרת לפני המעבר לתשלום באשראי - כדי שאפשר יהיה לפתוח את ההזמנה גם אם הדפדפן איבד אותם
public class PaymentDraft
{
    public string? Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Draft { get; set; } = string.Empty;
    public bool IsBatch { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
