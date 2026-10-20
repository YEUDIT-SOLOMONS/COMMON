namespace PhotoRental.Core.DTOs;

// מוצג ללקוח כאשר שולפים פריט ציוד צילום.
// שימו לב: אינו כולל את הכתובת המדויקת של הפריט (אין כאן שדה Address) - הכתובת נחשפת רק
// דרך GET /api/items/{id}/address, ורק לאחר שהשוכר בוחר באיסוף עצמי בתהליך ההשכרה (ראו ItemsController).
// כך כתובות המגורים של כל בעלי הציוד לא נחשפות בסתם בעת עיון בקטלוג.
public class ItemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DailyPrice { get; set; }

    // מחיר לשעה - אופציונלי, מוצג רק אם המשכיר בחר להפעיל תמחור שעתי
    public decimal? HourlyPrice { get; set; }

    // ItemDto: המחירים כוללים מע"מ. יצירה/עדכון: להוסיף מע"מ למחירים שנשלחו (לפני מע"מ)
    public bool AddVat { get; set; }
    public bool IsLocation { get; set; }
    public int Quantity { get; set; } = 1;
    public bool ForSale { get; set; }
    public decimal SalePrice { get; set; }

    // "New" / "LikeNew" / "Used" / "Damaged"
    public string? SaleCondition { get; set; }
    public bool Sold { get; set; }

    // "Hours" או "Days" - משך ההשכרה המינימלי שקבע המשכיר
    public string MinRentalUnit { get; set; } = "Days";
    public int MinRentalAmount { get; set; } = 1;

    public string SerialNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CategoryId { get; set; }
    public string? ExtraCategoryId { get; set; }
    public string? OwnerId { get; set; }
    public string? ImageDataUrl { get; set; }

    // כל תמונות הפריט לפי הסדר (הראשית ראשונה) - ריק אם אין תמונות. ImageDataUrl הוא תמיד הראשונה.
    public List<string> ImageDataUrls { get; set; } = new();

    // כמה תמונות יש לפריט. ברשימות הקטלוג מוחזרת רק הראשית (ImageDataUrl) והגלריה המלאה נשלפת עם הפריט עצמו
    public int ImageCount { get; set; }

    // כשהפריט מושכר כרגע: מתי ההשכרה אמורה להסתיים (שעון קיר של ישראל, בלי סימון UTC) - "יתפנה ב-..."
    public DateTime? BusyUntil { get; set; }

    // עיר הפריט בלבד - מוצגת תמיד. הכתובת המדויקת אינה כלולה כאן (ראו הערה למעלה).
    public string? City { get; set; }
    public DateTime? CreatedAtUtc { get; set; }

    // שם הרחוב בלבד, בלי מספר הבית - מוצג בחלון הפריט כדי לדעת את האזור בערים גדולות
    public string? Street { get; set; }
    public string? Neighborhood { get; set; }
}

// נשלח מהלקוח ביצירת פריט ציוד חדש
public class ItemCreateDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DailyPrice { get; set; }

    // הגדרות תמחור/השכרה מתקדמות - כולן אופציונליות, בשליטת המשכיר בלבד
    public decimal? HourlyPrice { get; set; }

    // ItemDto: המחירים כוללים מע"מ. יצירה/עדכון: להוסיף מע"מ למחירים שנשלחו (לפני מע"מ)
    public bool AddVat { get; set; }
    public int Quantity { get; set; } = 1;
    public bool ForSale { get; set; }
    public decimal SalePrice { get; set; }

    // "New" / "LikeNew" / "Used" / "Damaged"
    public string? SaleCondition { get; set; }
    public string MinRentalUnit { get; set; } = "Days";
    public int MinRentalAmount { get; set; } = 1;

    public string SerialNumber { get; set; } = string.Empty;
    public string? CategoryId { get; set; }
    public string? ExtraCategoryId { get; set; }
    public string? OwnerId { get; set; }

    // תמונת הפריט כ-Data URL (Base64), אופציונלי - נשלח מהלקוח לאחר כיווץ/שינוי גודל בדפדפן
    public string? ImageDataUrl { get; set; }

    // כמה תמונות לפריט (הראשונה היא הראשית). אם נשלח - גובר על ImageDataUrl הבודד.
    public List<string>? ImageDataUrls { get; set; }
}

// נשלח מהלקוח בעריכת פריט קיים ("עריכת פריט" באזור האישי) - אותם שדות כמו ביצירה, פרט לבעלים
// ולסטטוס (שאינם ניתנים לשינוי כאן). OwnerId נשלח רק לצורך אימות שהעורך הוא בעל הפריט.
public class ItemUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal DailyPrice { get; set; }
    public decimal? HourlyPrice { get; set; }

    // ItemDto: המחירים כוללים מע"מ. יצירה/עדכון: להוסיף מע"מ למחירים שנשלחו (לפני מע"מ)
    public bool AddVat { get; set; }
    public int Quantity { get; set; } = 1;
    public bool ForSale { get; set; }
    public decimal SalePrice { get; set; }

    // "New" / "LikeNew" / "Used" / "Damaged"
    public string? SaleCondition { get; set; }
    public string MinRentalUnit { get; set; } = "Days";
    public int MinRentalAmount { get; set; } = 1;
    public string? CategoryId { get; set; }
    public string? ExtraCategoryId { get; set; }
    public string? OwnerId { get; set; }
    public string? ImageDataUrl { get; set; }
    public List<string>? ImageDataUrls { get; set; }
}

// הכתובת המדויקת של הפריט - נחשפת רק דרך נקודת קצה ייעודית (GET /api/items/{id}/address),
// כדי שהלקוח יבקש אותה במפורש רק כאשר השוכר בוחר באיסוף עצמי, ולא תיחשף בעת עיון כללי בקטלוג.
public class ItemAddressDto
{
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? AddressNotes { get; set; }
}
