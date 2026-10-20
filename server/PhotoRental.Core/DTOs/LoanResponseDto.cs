namespace PhotoRental.Core.DTOs;

// פרטי עסקת השכרה כפי שהם מוצגים ללקוח.
// לשמירה על פרטיות: שם השוכרת אינו נכלל כאן בכלל, וגם שם וטלפון המשכירה אינם נחשפים לשוכרת -
// כל התיאום בין השתיים עובר דרך האתר (למניעת קשר מחוץ לו).
public class LoanResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string BorrowerId { get; set; } = string.Empty;
    public DateTime LoanDate { get; set; }
    public DateTime StartDate { get; set; }
    public int RentalDays { get; set; }
    public int Quantity { get; set; } = 1;
    public bool IsLocation { get; set; }

    // שעת תחילת ההשכרה ("HH:mm") כפי שנבחרה בעת ההזמנה - ריק בהשכרות ישנות
    public string? StartTime { get; set; }

    // איחור בהחזרה: כל יום איחור מחויב כיום השכרה נוסף. LateFee = LateDays x המחיר היומי (משולם ישירות למשכיר)
    public int LateDays { get; set; }

    // האם הועלתה אסמכתת העברה בנקאית להשכרה זו (את הקובץ עצמו מורידים דרך GET /api/loans/{id}/payment-proof)
    public bool HasPaymentProof { get; set; }

    // מתי המשכירה סימנה שהפריט נאסף (ריק אם עוד לא)
    public DateTime? PickedUpAt { get; set; }
    public decimal LateFee { get; set; }
    public DateTime ExpectedReturnDate { get; set; }
    public DateTime? ActualReturnDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DeliveryMethod { get; set; } = string.Empty;
    public bool ExpressDelivery { get; set; }

    // מיקום הפריט - העיר תמיד מוצגת; הכתובת המדויקת נכללת רק כאשר אופן הקבלה הוא איסוף עצמי
    public string? ItemCity { get; set; }
    public string? ItemAddress { get; set; }
    public string? ItemAddressNotes { get; set; }

    public string PaymentMethod { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string? PaymentTransactionId { get; set; }

    // פרטי חשבון הבנק של המשכיר לביצוע ההעברה - נכללים רק כאשר PaymentMethod="BankTransfer"
    public string? OwnerPayoutAccountHolderName { get; set; }
    public string? OwnerPayoutBankName { get; set; }
    public string? OwnerPayoutBranchNumber { get; set; }
    public string? OwnerPayoutAccountNumber { get; set; }

    // פרטי המשכיר לתיאום איסוף - נכללים רק כאשר DeliveryMethod=SelfPickup וגם PaymentStatus=Paid

    public decimal DeliveryFee { get; set; }
    public decimal ReturnFee { get; set; }

    // מחיר ההשכרה עצמה (ימים x מחיר יומי), בלי משלוח - הסכום לתשלום במזומן באיסוף עצמי
    public decimal RentalPrice { get; set; }

    // רק במבט המשכירה, בהשכרה ששולמה באשראי: מה שהאתר מעביר אליה (אחרי עמלה), מתי זה משתחרר, והאם כבר הועבר
    public decimal? OwnerPayoutNet { get; set; }
    public DateTime? OwnerPayoutRelease { get; set; }
    public bool OwnerPayoutPaid { get; set; }
    public bool OwnerPayoutHasProof { get; set; }

    // עד מתי אפשר לבטל (24 שעות לפני תחילת ההשכרה, שעון ישראל) - רק בהשכרה פעילה שטרם נאספה
    public DateTime? CancelDeadline { get; set; }
    public string ReturnMethod { get; set; } = string.Empty;
    public string? OutboundTrackingId { get; set; }
    public string? ReturnTrackingId { get; set; }

    // true = יש מזהה משלוח אצל הספק, ולכן אפשר להציג מעקב חי באתר (זמן הגעה, שליח ומיקום)
    public bool OutboundTrackable { get; set; }
    public bool ReturnTrackable { get; set; }

    // מזהה משותף כאשר השכרה זו נפתחה כחלק מהזמנה של כמה פריטים מאותו משכיר בבת אחת
    public string? OrderGroupId { get; set; }
}
