namespace PhotoRental.Core.DTOs;

// נשלח מהלקוח כדי לפתוח עסקת השכרה חדשה
public class LoanRequestDto
{
    public string ItemId { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;

    // תאריך תחילת ההשכרה שבחר השוכר (ברירת מחדל: היום) ומספר הימים המבוקש לשכירות
    public DateTime StartDate { get; set; }
    public int RentalDays { get; set; } = 1;
    public bool SameDayReturn { get; set; }

    // כמה יחידות של הפריט (כשיש למשכירה כמה עותקים)
    public int Quantity { get; set; } = 1;

    // שעת תחילת ההשכרה / האיסוף ("HH:mm") - אופציונלי; נשמרת ומוצגת לשוכר ולמשכיר (לא משפיעה על המחיר)
    public string? StartTime { get; set; }

    // אופן קבלת הפריט: "SelfPickup" (איסוף עצמי, ברירת המחדל) או "Delivery" (משלוח)
    public string DeliveryMethod { get; set; } = "SelfPickup";

    // משלוח אקספרס - רלוונטי רק כאשר DeliveryMethod="Delivery" ו-StartDate הוא היום
    public bool ExpressDelivery { get; set; }

    // אמצעי התשלום: "Cash" (רק באיסוף עצמי) / "BankTransfer" / "CreditCard"
    public string PaymentMethod { get; set; } = "Cash";

    // מזהה עסקת הסליקה - נדרש רק כאשר PaymentMethod="CreditCard", ומתקבל מראש מ-
    // POST /api/payments/upay/charge (ראו PaymentsController) לפני קריאה זו
    public string? PaymentTransactionId { get; set; }

    // אסמכתת העברה בנקאית (תמונה או PDF כ-Data URL) - חובה כש-PaymentMethod="BankTransfer"
    public string? PaymentProofDataUrl { get; set; }
    public string? PaymentProofFileName { get; set; }

    // כתובת השוכר המפורטת (עיר/רחוב/מספר/קומה) - נדרשת כאשר DeliveryMethod="Delivery" (יעד
    // המשלוח) ו/או ReturnMethod="CourierPickup" (כתובת האיסוף בהחזרה)
    public DeliveryAddressDto? DeliveryAddress { get; set; }

    // כתובת אחרת לאיסוף ההחזרה (אופציונלי) - כשהשוכרת רוצה שהשליח יאסוף את הפריט ממקום שונה מזה שאליו נשלח.
    // רלוונטי רק כש-DeliveryMethod="Delivery" ו-ReturnMethod="CourierPickup"; אם לא נשלחה - נאסף מכתובת המשלוח
    public DeliveryAddressDto? ReturnPickupAddress { get; set; }

    // אופן ההחזרה בתום ההשכרה - "SelfDropoff" (ברירת המחדל) או "CourierPickup", נקבע כבר עכשיו
    public string ReturnMethod { get; set; } = "SelfDropoff";

    // שעת ההחזרה המשוערת ("HH:mm") - אופציונלי, ברירת מחדל 12:00 אם לא נשלח
    public string? ReturnTime { get; set; }

    public bool TermsAccepted { get; set; }
    public bool PhoneConfirmed { get; set; }
    public string? ShippingPaymentId { get; set; }
}

// נשלח מהלקוח כדי לפתוח בבת אחת כמה עסקאות השכרה על כמה פריטים מאותו חבר קהילה (משכיר) -
// "הזמנה" אחת המשתפת מועדים, אמצעי תשלום, ועלות משלוח בודדת (ולא כפולה) עבור כל הפריטים.
// כל שאר השדות זהים במשמעותם ל-LoanRequestDto.
public class LoanBatchRequestDto
{
    public List<string> ItemIds { get; set; } = new();

    // כמות לכל פריט (מזהה פריט -> יחידות). פריט שלא מופיע - יחידה אחת
    public Dictionary<string, int>? Quantities { get; set; }
    public string BorrowerId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public int RentalDays { get; set; } = 1;
    public bool SameDayReturn { get; set; }
    public string? StartTime { get; set; }
    public string DeliveryMethod { get; set; } = "SelfPickup";
    public bool ExpressDelivery { get; set; }
    public string PaymentMethod { get; set; } = "Cash";
    public string? PaymentTransactionId { get; set; }

    // אסמכתת העברה בנקאית (תמונה או PDF כ-Data URL) - חובה כש-PaymentMethod="BankTransfer"
    public string? PaymentProofDataUrl { get; set; }
    public string? PaymentProofFileName { get; set; }
    public DeliveryAddressDto? DeliveryAddress { get; set; }
    public DeliveryAddressDto? ReturnPickupAddress { get; set; }
    public string ReturnMethod { get; set; } = "SelfDropoff";
    public string? ReturnTime { get; set; }
    public bool TermsAccepted { get; set; }
    public bool PhoneConfirmed { get; set; }
    public string? ShippingPaymentId { get; set; }
}
