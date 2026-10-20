namespace PhotoRental.Core.Entities;

using PhotoRental.Core.Enums;

public class Loan
{
    public string? Id { get; set; }

    // מזהה העותק המושכר
    public string? ItemCopyId { get; set; }

    // מזהה המשתמש השוכר
    public string? BorrowerId { get; set; }

    public DateTime LoanDate { get; set; }

    // תאריך תחילת ההשכרה שנבחר ע"י השוכר (יכול להיות היום או תאריך עתידי) ומספר הימים המבוקש -
    // ExpectedReturnDate מחושב מהם אוטומטית בשכבת השירות (StartDate + RentalDays).
    public DateTime StartDate { get; set; }

    public int RentalDays { get; set; } = 1;

    // כמה יחידות של הפריט הושכרו בהשכרה הזו
    public int Quantity { get; set; } = 1;
    public int Units => Math.Max(1, Quantity);

    // מחיר ליחידת השכרה (יום) כפי שנקבע בהזמנה. בסטודיו/לוקיישן: מחיר ההזמנה כולה (לפי שעות)
    public decimal? DailyRate { get; set; }
    public decimal RateOr(decimal itemDailyPrice) => DailyRate ?? itemDailyPrice;

    // השכרת סטודיו/לוקיישן: לפי שעות, ביום אחד, הגעה עצמית
    public bool IsLocation { get; set; }

    // שעת תחילת ההשכרה/האיסוף ("HH:mm") - אופציונלי
    public string? StartTime { get; set; }

    // אחוז עמלת השירות שנקבע ברגע פתיחת ההשכרה (למשל 0.07) - נשמר כדי ששינוי עתידי באחוז לא ישנה חיובי עבר.
    // ריק בהשכרות ישנות - ואז חל האחוז הנוכחי.
    public decimal? CommissionRate { get; set; }

    // ימי איחור בהחזרה (כל יום או חלק מיום מעבר למועד ההחזרה נספר כיום השכרה נוסף) - נקבע ברגע ההחזרה בפועל
    public int LateDays { get; set; }

    // תוספות ששולמו באשראי דרך האתר (השכרה ששולמה באשראי): איחור בהחזרה והארכות
    public decimal LateFeeCharged { get; set; }
    public decimal ExtensionChargedTotal { get; set; }

    // אסמכתת ההעברה הבנקאית שהועלתה בעת ההזמנה (ראו PaymentProof) - רק בתשלום בהעברה
    public string? PaymentProofId { get; set; }

    // טוקן סודי לקישורי "חלון הסימון" שנשלחים למשכירה במייל (בלי התחברות): הפריט נאסף / תשלום התקבל
    public string? OwnerActionToken { get; set; }

    // מתי המשכירה סימנה שהפריט נאסף
    public DateTime? PickedUpAt { get; set; }

    // מי סימנה שהפריט נאסף: "Owner" / "Renter"
    public string? PickedUpBy { get; set; }

    // מתי נשלח למשכירה המייל "הפריט נאסף?" (רבע שעה אחרי תחילת ההשכרה) - פעם אחת
    public DateTime? PickupPromptSentAt { get; set; }

    // מתי נשלח למשכירה המייל "האם X הוחזר?" (חצי שעה אחרי מועד ההחזרה) - פעם אחת
    public DateTime? ReturnPromptSentAt { get; set; }

    // בקשת הארכה של השוכרת (בשיחת התזכורת): Pending (ממתינה לאישור המשכירה בשיחה) / Approved / Declined / Expired
    public string? ExtensionStatus { get; set; }
    public int ExtensionDays { get; set; }
    public DateTime? ExtensionRequestedAt { get; set; }

    // כמה פעמים חויגה המשכירה לאישור ההארכה (ניסיון חוזר אחד אם לא ענתה)
    public int ExtensionAttempts { get; set; }

    // מתי המשכירה סימנה בחלון שהפריט עדיין לא הוחזר (אז נשלחו לשוכרת מייל ושיחה, והצוות עודכן)
    public DateTime? NotReturnedReportedAt { get; set; }

    public DateTime ExpectedReturnDate { get; set; }

    public DateTime? ActualReturnDate { get; set; }

    public LoanStatus Status { get; set; } = LoanStatus.Active;

    // אופן קבלת הפריט: איסוף עצמי או משלוח
    public DeliveryMethod DeliveryMethod { get; set; } = DeliveryMethod.SelfPickup;

    // משלוח אקספרס (תוך זמן קצר) - רלוונטי רק כאשר אופן הקבלה הוא משלוח ותאריך ההתחלה הוא היום.
    public bool ExpressDelivery { get; set; }

    // עלות המשלוח (₪) - מחושבת ונשמרת בעת יצירת ההשכרה, ומתווספת לסכום הכולל. 0 באיסוף עצמי.
    public decimal DeliveryFee { get; set; }

    // כתובת השוכר - נדרשת רק כאשר יש מעורבות שליח בהובלה (משלוח יוצא ו/או איסוף שליח בהחזרה)
    public string? DeliveryAddress { get; set; }

    // כתובת איסוף ההחזרה כשהיא שונה מכתובת המשלוח (ריק = נאסף מכתובת המשלוח)
    public string? ReturnPickupAddress { get; set; }

    public DateTime? TermsAcceptedAtUtc { get; set; }
    public string? TermsVersion { get; set; }
    public DateTime? PhoneConfirmedAtUtc { get; set; }

    public string? ConfirmedPhone { get; set; }

    public decimal ReturnFee { get; set; }

    public string? ShippingChargeStatus { get; set; }
    public string? ShippingPaymentId { get; set; }
    public decimal ShippingChargedAmount { get; set; }

    public DateTime? OwnerPayoutPaidAtUtc { get; set; }
    public decimal OwnerPayoutAmount { get; set; }

    // אסמכתת ההעברה למשכירה (אוסף PaymentProofs)
    public string? OwnerPayoutProofId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledBy { get; set; }

    // אופן ההחזרה בתום ההשכרה - נקבע מראש יחד עם מועד ההחזרה המשוער (ExpectedReturnDate)
    public ReturnMethod ReturnMethod { get; set; } = ReturnMethod.SelfDropoff;

    // מזהי מעקב משלוח מדומים (ראו SimulatedShippingService) - המשלוח היוצא (אם DeliveryMethod=Delivery)
    // ואיסוף השליח בהחזרה (אם ReturnMethod=CourierPickup), כל אחד מתוזמן ונשלח אוטומטית כבר בעת יצירת ההשכרה.
    public string? OutboundTrackingId { get; set; }

    public string? ReturnTrackingId { get; set; }

    // מזהי המשלוחים אצל חברת השילוח (GetPackage) - לשליפת מצב חי: זמן הגעה, שליח ומיקום (ראו IShippingService.GetStatusAsync)
    public string? OutboundProviderId { get; set; }

    // אקספרס ליום עתידי: השליח מוזמן ביום ההשכרה עצמו (שעון ישראל), כי אקספרס יוצא מיד עם ההזמנה
    public DateTime? ExpressDispatchAtLocal { get; set; }
    public string? ReturnProviderId { get; set; }

    // מועד שליחת תזכורת ההחזרה לשוכר (ראו ReturnReminderBackgroundService) - null כל עוד לא נשלחה,
    // מונע שליחה כפולה של אותה תזכורת.
    public DateTime? ReturnReminderSentAt { get; set; }

    // אמצעי התשלום שבחר השוכר: מזומן (רק באיסוף עצמי) / העברה בנקאית / כרטיס אשראי
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    // מזהה עסקת הסליקה שהתקבל מ-SimulatedUpayPaymentService - קיים רק כאשר PaymentMethod=CreditCard
    public string? PaymentTransactionId { get; set; }

    // מזהה משותף לכמה עסקאות השכרה שנפתחו יחד כהזמנה אחת (כמה פריטים מאותו משכיר בהזמנה
    // בודדת - ראו LoanService.CreateBatchAsync). null עבור השכרה בודדת רגילה.
    public string? OrderGroupId { get; set; }

    // אובייקטי ניווט נוחים - מאוכלסים ידנית ע"י שכבת השירות (Service) לצורך תצוגה,
    // ואינם נשמרים ב-Firestore עצמו (רק המזהים ItemCopyId/BorrowerId נשמרים).
    public Item? Item { get; set; }
    public User? Borrower { get; set; }
}
