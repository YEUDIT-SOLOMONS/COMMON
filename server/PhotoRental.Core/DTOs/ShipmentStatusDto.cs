namespace PhotoRental.Core.DTOs;

// מצב משלוח חי כפי שמוחזר מחברת השילוח (GetPackage) - לתצוגה באתר: סטטוס, זמן הגעה, שליח ומיקום
public class ShipmentStatusDto
{
    // סטטוס מנורמל: Pending / Assigned / OnTheWay / PickedUp / Delivered / Canceled / Failed
    public string Status { get; set; } = "Pending";

    // תיאור בעברית לתצוגה
    public string StatusLabel { get; set; } = string.Empty;

    // תאריך וחלון זמן משוער (למשל "10:00-13:00") - יכול להיות ריק (באקספרס אין חלון קבוע)
    public DateTime? EtaDate { get; set; }
    public string? EtaTimeRange { get; set; }

    public string? CourierName { get; set; }
    public string? CourierPhone { get; set; }

    // מיקום השליח האחרון (אם נמסר) ומתי עודכן
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime? LocationUpdatedAt { get; set; }

    // הערכת הגעה חיה לפי מיקום השליח: לאן הוא נוסע עכשיו ("Pickup" = לאיסוף, "Dropoff" = ליעד), מרחק נותר בק"מ
    // (כביש משוער), דקות משוערות להגעה, ואחוז ההתקדמות בנסיעה עם החבילה (0..1) - לחיווי הנסיעה באתר
    public string? LegTarget { get; set; }
    public double? RemainingKm { get; set; }
    public int? EtaMinutes { get; set; }
    public double? Progress { get; set; }

    // דף המעקב של חברת השילוח
    public string? TrackingUrl { get; set; }

    // כשלא ניתן לקבל מצב חי (מזהה ישן, חיבור לא פעיל או שגיאה זמנית) - הסיבה לתצוגה
    public string? Note { get; set; }
}

// מצב שני הכיוונים של ההשכרה: המשלוח אל השוכרת, ואיסוף ההחזרה
public class LoanShippingStatusDto
{
    public ShipmentStatusDto? Outbound { get; set; }
    public ShipmentStatusDto? Return { get; set; }
}
