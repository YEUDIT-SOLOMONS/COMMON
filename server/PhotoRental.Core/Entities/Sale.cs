namespace PhotoRental.Core.Entities;

// מכירת פריט יד 2 דרך האתר (6% עמלה מהמוכרת)
public class Sale
{
    public string? Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    // כמה פריטים מאותה מוכרת שנקנו יחד: תשלום אחד ומשלוח אחד (עלות המשלוח נשמרת ברשומה הראשונה)
    public string? OrderId { get; set; }
    public string SellerId { get; set; } = string.Empty;
    public string BuyerId { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal DeliveryFee { get; set; }
    public decimal CommissionRate { get; set; }
    public decimal Commission { get; set; }
    public decimal SellerAmount { get; set; }

    // "SelfPickup" / "Delivery"
    public string DeliveryMethod { get; set; } = "SelfPickup";
    public string? DeliveryAddress { get; set; }
    public string? TrackingId { get; set; }
    public string? ProviderId { get; set; }
    public string? DispatchError { get; set; }

    // "Cash" / "BankTransfer" / "CreditCard". במזומן ובהעברה הכסף עובר ישירות למוכרת והעמלה נכללת בדוח החודשי
    public string PaymentMethod { get; set; } = "CreditCard";
    public string PaymentTransactionId { get; set; } = string.Empty;
    public string? PaymentProofId { get; set; }

    // העברה בנקאית עם משלוח: דמי המשלוח נגבים מכרטיס שמור (כמו בהשכרות)
    public string? ShippingChargeStatus { get; set; }
    public string? ShippingPaymentId { get; set; }

    // "Paid" / "Received" / "Problem"
    public string Status { get; set; } = "Paid";
    public string? ProblemText { get; set; }
    public string BuyerToken { get; set; } = string.Empty;
    public string SellerToken { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public bool AutoConfirmed { get; set; }
    public DateTime? BuyerReminderSentAtUtc { get; set; }

    // סימון המוכרת: מסרה את הפריט / עוד לא נמסר (מהמייל הראשון, או מהתזכורת אחרי יומיים)
    public DateTime? HandedOverAtUtc { get; set; }
    public DateTime? NotHandedAtUtc { get; set; }
    public DateTime? SellerPaidAtUtc { get; set; }
}
