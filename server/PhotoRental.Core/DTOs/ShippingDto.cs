namespace PhotoRental.Core.DTOs;

// כתובת מסירה מפורטת (עיר/רחוב/מספר בית/קומה) - עדיפה על מחרוזת חופשית אחת, כי חברת השילוח
// (GetPackage) דורשת כתובת "מזוהה" (עיר+רחוב+מספר) כדי לתת מחיר מדויק ולבצע איסוף/מסירה תקינים
// בפועל; ניסיון לפצל מחרוזת חופשית לרכיבים אלה בשרת (למשל לפי פסיק) התברר כלא אמין מול כתובות
// אמיתיות בעברית (ראו GetPackageShippingService). קומה אינה חלק מהכתובת עצמה מבחינת GetPackage -
// היא מועברת כהערה לשליח בנפרד.
public class DeliveryAddressDto
{
    public string City { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string HouseNumber { get; set; } = string.Empty;
    public string? Floor { get; set; }

    // מספר דירה או שם משפחה - כדי שהשליח ימצא את הדלת
    public string? Apartment { get; set; }
}

// כתובת עבור IShippingService - עשויה להיות כתובת מפורטת של השוכר (City/Street/HouseNumber, ראו
// DeliveryAddressDto) או כתובת חופשית קיימת של המשכיר (FreeText, כפי שכבר שמורה ב-Item.Address).
// נדרש "איחוד" כזה כי באיסוף החזרה השוכר הוא נקודת ה"איסוף" והמשכיר נקודת ה"מסירה" - הפוך
// מהמשלוח היוצא - כך ששני הצדדים (Pickup/Dropoff) ב-IShippingService צריכים להיות מסוגלים
// להכיל את שני הסוגים, בהתאם לכיוון בפועל.
public class ShippingAddressDto
{
    public string? FreeText { get; set; }
    public string? City { get; set; }
    public string? Street { get; set; }
    public string? HouseNumber { get; set; }
    public string? Floor { get; set; }
    public string? Apartment { get; set; }

    public string? ContactName { get; set; }
    public string? ContactPhone { get; set; }

    // הערות על הכתובת (למשל מהמשכירה) - מועברות לשליח
    public string? Notes { get; set; }

    public static ShippingAddressDto FromFreeText(string freeText, string? notes = null) => new() { FreeText = freeText, Notes = notes };

    public static ShippingAddressDto FromDetails(DeliveryAddressDto details) => new()
    {
        City = details.City,
        Street = details.Street,
        HouseNumber = details.HouseNumber,
        Floor = details.Floor,
        Apartment = details.Apartment
    };

    public ShippingAddressDto WithContact(string? name, string? phone) => new()
    {
        FreeText = FreeText,
        City = City,
        Street = Street,
        HouseNumber = HouseNumber,
        Floor = Floor,
        Apartment = Apartment,
        Notes = Notes,
        ContactName = name,
        ContactPhone = phone
    };
}

// נשלח מהלקוח כדי לקבל הצעת מחיר אמיתית למשלוח (רגיל ואקספרס) עוד לפני אימות זמינות ותשלום -
// כתובת המסירה בלבד. כתובת האיסוף (מיקום הפריט) נלקחת בשרת מתוך הפריט עצמו ולא מגיעה מהלקוח -
// כדי לא לחשוף את הכתובת המדויקת של המשכיר לפני הזמן (ראו מדיניות הפרטיות ב-ItemDto/LoanResponseDto).
public class ShippingQuoteRequestDto
{
    public string ItemId { get; set; } = string.Empty;
    public DeliveryAddressDto DeliveryAddress { get; set; } = new();

    // מתי השוכרת צריכה את הפריט בידיים (תאריך yyyy-MM-dd ושעה HH:mm) - לבדיקה שהמשלוח אכן יגיע עד אז
    public string? StartDate { get; set; }
    public string? StartTime { get; set; }

    public DeliveryAddressDto? ReturnPickupAddress { get; set; }

    public string? ReturnDate { get; set; }
    public string? ReturnTime { get; set; }
}

// תוצאת בדיקת "האם המשלוח יגיע עד השעה המבוקשת". Verified=false = לא הצלחנו לבדוק מול חברת השילוח
// (ההזמנה לא נחסמת, אך אין הבטחה); Possible=false = בטוח שלא יגיע בזמן - חוסמים ומסבירים
public class ArrivalCheckDto
{
    public bool Possible { get; set; } = true;
    public bool Verified { get; set; }

    // חלון ההגעה שנבחר ("HH:MM-HH:MM") כשהמשלוח הרגיל מובטח עד השעה המבוקשת
    public string? TimeRange { get; set; }
    public string? Message { get; set; }

    // השעה המוקדמת ביותר שאפשר להבטיח (סוף חלון ההגעה) כשהשעה שביקשה אינה אפשרית באותו יום - "HH:mm"
    public string? SuggestedTime { get; set; }

    // שעת הגעה משוערת ("HH:mm") למשלוח אקספרס, לפי משך המסלול שחברת השילוח מחזירה
    public string? EstimatedArrival { get; set; }
}

// מחיר משלוח אמיתי (רגיל ואקספרס) - מוצג ללקוח לפני אימות הזמינות כדי שיידע בדיוק כמה ישלם,
// ומשמש גם בפועל לחישוב הסכום שנגבה בעת פתיחת ההשכרה (LoanService) - כך שהמחיר המוצג תמיד
// זהה למחיר שנגבה בפועל, ולא הערכה נפרדת שעלולה לא להתאים.
public class ShippingQuoteDto
{
    public decimal RegularPrice { get; set; }
    public decimal ExpressPrice { get; set; }

    // האם המשלוח (רגיל / אקספרס) יגיע עד השעה שהשוכרת ביקשה - רק כשנשלחו תאריך ושעה בבקשה
    public ArrivalCheckDto? RegularArrival { get; set; }
    public ArrivalCheckDto? ExpressArrival { get; set; }

    public decimal ReturnPrice { get; set; }

    public ArrivalCheckDto? ReturnArrival { get; set; }
}
