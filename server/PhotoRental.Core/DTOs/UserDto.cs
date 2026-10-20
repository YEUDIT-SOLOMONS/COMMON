namespace PhotoRental.Core.DTOs;

// מוצג ללקוח כאשר שולפים משתמש - אינו כולל את פרטי חשבון הבנק לקבלת תשלומים (ראו UserPayoutInfoDto)
// כדי לא לחשוף אותם בכל שליפת משתמש כללית (למשל הצגת שם בעל ציוד בפרטי פריט)
public class UserDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? AddressNotes { get; set; }
    public string? AddressFloor { get; set; }
    public string? AddressApartment { get; set; }
    public DateTime? CommissionConsentAtUtc { get; set; }
    public DateTime? SaleCommissionConsentAtUtc { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public string? QuietFrom { get; set; }
    public string? QuietTo { get; set; }
}

// נשלח מהלקוח כדי לשמור כתובת מגורים ועיר עבור המשתמש (המשכיר) - נדרש לפני הוספת הפריט הראשון להשכרה,
// וניתן לעדכן דרך אותה נקודת קצה גם בהמשך מהאזור האישי
public class UserAddressUpdateDto
{
    public string Address { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string? AddressNotes { get; set; }
    public string? AddressFloor { get; set; }
    public string? AddressApartment { get; set; }
    public string? Street { get; set; }
}

// נשלח מהלקוח כדי לעדכן פרטים אישיים (שם/טלפון) מהאזור האישי, בכל עת
public class UserProfileUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    // null = לא נשלח (לא משנים את ההגדרה הקיימת)
    public bool? QuietHoursEnabled { get; set; }
    public string? QuietFrom { get; set; }
    public string? QuietTo { get; set; }
}

// נשלח מהלקוח בעת הרשמת משתמש חדש
public class UserCreateDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    // התפקיד המבוקש: "Owner" (משכיר) / "Renter" (שוכר) / "Admin". ברירת מחדל: Renter
    public string Role { get; set; } = "Renter";
}

// פרטי חשבון הבנק של המשכיר לקבלת תשלומים עבור השכרות, כפי שהם מוצגים/נשלחים ללקוח.
// IsConfigured=false כאשר המשכיר עדיין לא הזין פרטי חשבון בכלל.
public class UserPayoutInfoDto
{
    public bool IsConfigured { get; set; }
    public string AccountHolderName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BranchNumber { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;

}

// נשלח מהלקוח כדי להגדיר/לעדכן את פרטי חשבון הבנק לקבלת תשלומים
public class UserPayoutUpdateDto
{
    public string AccountHolderName { get; set; } = string.Empty;
    public string BankName { get; set; } = string.Empty;
    public string BranchNumber { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;

    // חובה true: המשכיר סימן שהוא מסכים לשמירת הפרטים ולהצגתם לשוכר בתשלום בהעברה בנקאית
    public bool ConsentAccepted { get; set; }
}
