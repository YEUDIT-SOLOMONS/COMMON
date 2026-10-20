namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

// דיספאץ' (שליחה אוטומטית) של בקשת איסוף/משלוח לחברת שילוח, וקבלת הצעת מחיר אמיתית - מדומה
// כרגע כברירת מחדל (ראו SimulatedShippingService), או אמיתי דרך GetPackage אם הוגדרו פרטי
// התחברות (ראו GetPackageShippingService). ה-LoanService וה-Frontend אינם צריכים לדעת איזה
// מהשניים פעיל בפועל.
public interface IShippingService
{
    // הצעת מחיר (רגיל/אקספרס) למשלוח בין שתי הכתובות - ללא התחייבות/דיספאץ' בפועל. משמש הן
    // להצגת המחיר האמיתי ללקוח לפני אימות הזמינות, והן לחישוב הסכום שנגבה בפועל בעת פתיחת ההשכרה.
    Task<ShippingQuoteDto> GetQuoteAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress);

    // isExpress קובע אצל GetPackage בין שני מוצרי המשלוח שלהם: "Shared Route" (מסלול משותף
    // מתוזמן, זול יותר - false) או "Express" (מסלול ייעודי מהיר, יקר יותר - true) - ראו הערה
    // מפורטת ב-GetPackageShippingService
    // mustArriveBy: השעה (שעון ישראל) שעד אליה המשלוח חייב להגיע ביום scheduledDate. אם אי אפשר להבטיח - זורק InvalidOperationException
    Task<ShipmentRequestResult> RequestPickupAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress, DateTime scheduledDate, bool isExpress, TimeSpan? mustArriveBy = null);

    // בדיקה מראש: האם משלוח (רגיל/אקספרס) לכתובת הזו יגיע עד mustArriveBy ביום date
    Task<ArrivalCheckDto> CheckArrivalAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress, DateTime date, TimeSpan mustArriveBy, bool isExpress);

    // מצב משלוח חי לפי המזהה אצל הספק (ShipmentRequestResult.ProviderId): סטטוס, זמן הגעה משוער, שליח ומיקום. null אם לא ידוע
    Task<ShipmentStatusDto?> GetStatusAsync(string providerId);

    // ביטול משלוח/איסוף שהוזמן (לפי המזהה אצל הספק). זורק InvalidOperationException אם הביטול נכשל
    Task CancelAsync(string providerId);
}
