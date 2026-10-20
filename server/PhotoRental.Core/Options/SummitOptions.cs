namespace PhotoRental.Core.Options;

// חיבור ל-SUMIT (סאמיט) - סליקת אשראי לתשלום עמלת השירות החודשית. כל עוד CompanyId/ApiKey אינם מוגדרים,
// הדוח החודשי כולל רק פרטי העברה בנקאית (בלי כפתור תשלום באשראי).
//  - CompanyId: מזהה העסק בסאמיט
//  - ApiKey: מפתח התחברות פרטי (סוד - נשמר ב-Secret Manager בלבד, לעולם לא בקוד או ב-appsettings)
//  - PublicKey: מפתח התחברות ציבורי (נשמר לשימוש עתידי בהטמעת טופס תשלום באתר; לא נדרש לתהליך הנוכחי)
public class SummitOptions
{
    public long CompanyId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.sumit.co.il";

    // סוג המסמך שמופק אוטומטית אחרי תשלום (נקבע בבקשה עצמה - לא תלוי בהגדרות המערכת): 1 = חשבונית מס/קבלה
    // (אצל עוסק פטור: חשבון/קבלה), 2 = קבלה בלבד. ריק = ברירת המחדל של העסק בסאמיט.
    public int? DocumentType { get; set; } = 1;
}
