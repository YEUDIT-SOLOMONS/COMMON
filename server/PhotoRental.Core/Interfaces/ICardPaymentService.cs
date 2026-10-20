namespace PhotoRental.Core.Interfaces;

// בקשה ליצירת דף תשלום באשראי עם סכום קבוע (המשלמת לא מקלידה סכום)
public class CardPaymentRequest
{
    public decimal Amount { get; set; }

    // שם הפריט בחשבונית/קבלה
    public string Description { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }

    // מזהה הלקוחה אצלנו (כך שתשלומים חוזרים של אותה משכירה משויכים לאותו כרטיס לקוח)
    public string CustomerExternalId { get; set; } = string.Empty;

    // מזהה התשלום אצלנו - חוזר אלינו בכתובת החזרה ונשמר בתשלום (לאימות)
    public string ExternalIdentifier { get; set; } = string.Empty;

    public string RedirectUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;

    // לשמור את הכרטיס אצל הספק (אחרי הסכמה מפורשת) לחיוב אוטומטי עתידי
    public bool SavePaymentMethod { get; set; }
}

// חיוב כרטיס שמור (בלי שהמשלמת נוכחת) - לפי הסכמה שנתנה בתשלום הראשון
public class CardChargeRequest
{
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string CustomerExternalId { get; set; } = string.Empty;
}

public class CardChargeResult
{
    public bool Success { get; set; }
    public long? PaymentId { get; set; }
    public string? Error { get; set; }
}

// פרטי תשלום כפי שמוחזרים מהספק בבדיקה בין שרת לשרת
public class CardPaymentInfo
{
    public long PaymentId { get; set; }
    public bool Valid { get; set; }
    public decimal Amount { get; set; }
    public string? ExternalIdentifier { get; set; }
    public string? Status { get; set; }
}

// סליקת אשראי באמצעות דף תשלום מאובטח של הספק (SUMIT)
public interface ICardPaymentService
{
    // false כשלא הוגדר חיבור אמיתי (חסרים מפתחות)
    bool IsEnabled { get; }

    // יוצר דף תשלום חדש עם הסכום המדויק ומחזיר את כתובתו (המשלמת מועברת אליה). זורק InvalidOperationException בכשל
    Task<string> BeginPaymentAsync(CardPaymentRequest request);

    // מחייב את הכרטיס השמור של הלקוחה (הספק מפיק ושולח חשבונית/קבלה). לא זורק - מחזיר Success=false עם הסיבה
    Task<CardChargeResult> ChargeSavedCardAsync(CardChargeRequest request);

    // מאמת מול הספק פרטי תשלום לפי מזהה (null אם לא נמצא)
    Task<CardPaymentInfo?> GetPaymentAsync(long paymentId);
}
