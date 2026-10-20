namespace PhotoRental.Core.Entities;

using PhotoRental.Core.Enums;

public class Item
{
    public string? Id { get; set; }

    // מונה גרסה שעולה בכל השכרה של הפריט - זה המשאב שמתחרים עליו
    public long Version { get; set; }

    // updateTime של המסמך ב-Firestore בזמן הקריאה. לא נשמר כשדה - משמש כ-concurrency token בשמירה
    public string? ConcurrencyToken { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal DailyPrice { get; set; }

    // מחיר לשעה - אופציונלי, נקבע ע"י המשכיר בלבד אם הוא מעוניין לאפשר גם השכרה לפי שעות.
    // null = המשכיר לא הפעיל תמחור שעתי, ורק DailyPrice רלוונטי.
    public decimal? HourlyPrice { get; set; }

    // המחירים (יומי ושעתי) כוללים מע"מ שהמשכירה ביקשה להוסיף
    public bool VatAdded { get; set; }

    // כמה עותקים פיזיים של הפריט יש למשכירה
    public int Quantity { get; set; } = 1;

    // פריט בקטגוריית סטודיו/לוקיישן: HourlyPrice = מחיר לשעה, DailyPrice = מחיר ליום שלם
    public bool IsLocation { get; set; }

    // יד 2: פריט למכירה (ולא להשכרה)
    public bool ForSale { get; set; }
    public decimal SalePrice { get; set; }
    public string? SaleCondition { get; set; }
    public DateTime? SoldAtUtc { get; set; }

    // קטגוריה נוספת שהפריט מתאים לה (מוצג בקטלוג גם בה)
    public string? ExtraCategoryId { get; set; }

    // משך ההשכרה המינימלי שהמשכיר דורש (למשל: לפחות 2 ימים, או לפחות 3 שעות) - אופציונלי, ברירת מחדל: יום אחד.
    public RentalUnit MinRentalUnit { get; set; } = RentalUnit.Days;

    public int MinRentalAmount { get; set; } = 1;

    public string SerialNumber { get; set; } = string.Empty;

    public CopyStatus Status { get; set; } = CopyStatus.Available;

    // קטגוריה ובעלים - נשמרים כמזהים (Foreign Keys) בלבד,
    // כמקובל במסד נתונים מסוג מסמכים (Firestore) ולא כרשומות משוכפלות.
    public string? CategoryId { get; set; }

    public string? OwnerId { get; set; }

    // תמונת הפריט, מקודדת כ-Data URL (Base64) ונשמרת ישירות כשדה מחרוזת במסמך ה-Firestore -
    // אין צורך בשירות אחסון קבצים נפרד (Firebase Storage) עבור תמונות קטנות של פריט בודד.
    // ריק/null = אין תמונה (מוצג אייקון ברירת מחדל בצד הלקוח).
    public string? ImageDataUrl { get; set; }

    // תמונות נוספות של הפריט (מעבר לראשית, ImageDataUrl) - עד 5, נשמרות כמחרוזת JSON אחת במסמך
    public List<string> ExtraImageDataUrls { get; set; } = new();

    // עיר וכתובת מדויקת - מועתקות אוטומטית מכתובת המגורים של המשכיר (User.City / User.Address) בעת
    // יצירת הפריט. העיר מוצגת בגלוי לכל המשתמשים בכרטיס הפריט; הכתובת המדויקת נחשפת ללקוח רק
    // בהקשר של השכרת פריט ספציפי, ומוצגת בממשק רק כאשר השוכר בוחר באיסוף עצמי.
    public string? City { get; set; }

    public string? Address { get; set; }

    // שם הרחוב בלבד - מוצג בחלון הפריט (בלי מספר הבית)
    public string? Street { get; set; }

    // מחיקת פריט היא "מחיקה רכה": הפריט נשאר במסד הנתונים כדי שהיסטוריית ההשכרות שלו (ודוחות העמלה) תישמר לתמיד,
    // ורק מוסתר מהקטלוג ומרשימת הציוד של הבעלים. null = הפריט פעיל.
    public DateTime? DeletedAtUtc { get; set; }

    // כמה פעמים הפריט הושכר (למיון "המבוקשים בקהילה"). null = עדיין לא חושב; יחושב פעם אחת מההשכרות הקיימות
    public int? RentalCount { get; set; }

    // ===== בדיקת "פריט שלא הושכר" (ראו StaleItemService) =====
    // מתי הפריט נוסף (פריטים ישנים מקבלים חותמת בבדיקה הראשונה - ממנה מתחיל מניין 6 השבועות)
    public DateTime? CreatedAtUtc { get; set; }

    // מתי הבעלים אישרה בקישור מהמייל שהפריט עדיין זמין
    public DateTime? AvailabilityConfirmedAtUtc { get; set; }

    // מתי נשלח לבעלים המייל "הפריט לא הושכר ב-6 השבועות האחרונים" והטוקן הסודי לקישורים שבו
    public DateTime? StaleNoticeSentAtUtc { get; set; }
    public string? StaleActionToken { get; set; }

    // אובייקטי ניווט נוחים לשימוש בצד השרת - מאוכלסים ידנית ע"י שכבת השירות (Service)
    // בעת הצורך, ואינם נשמרים ב-Firestore עצמו (כדי למנוע הקשר מעגלי ושכפול נתונים).
    public Category? Category { get; set; }
    public User? Owner { get; set; }
}
