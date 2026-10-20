namespace PhotoRental.Core.Entities;

using PhotoRental.Core.Enums;

public class User
{
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    // תפקיד המשתמש: Renter (שוכר) / Owner (משכיר) / Admin (מנהל)
    public UserRole Role { get; set; } = UserRole.Renter;

    // גיבוב הסיסמה (PBKDF2) - לעולם לא הסיסמה עצמה. משמש להתחברות (ראו PhotoRental.Core.Security.PasswordHasher).
    // אינו נחשף ללקוח בשום DTO (UserDto אינו כולל שדה זה).
    public string? PasswordHash { get; set; }

    // כתובת מגורים מלאה ועיר - נאספות פעם אחת מהמשכיר בעת הוספת הפריט הראשון להשכרה (ראו
    // UserService.UpdateAddressAsync), ומשמשות למילוי אוטומטי של מיקום הפריט (Item.City / Item.Address).
    // מעקב למנהלות: מתי נרשם החשבון ומתי התחבר לאחרונה (ריק בחשבונות שנוצרו לפני הוספת המעקב)
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    // מתי המשכירה אישרה את תנאי עמלת השירות (בפעם הראשונה שהוסיפה פריט)
    public DateTime? CommissionConsentAtUtc { get; set; }

    // אישור עמלת המכירה ביד 2 (פעם אחת, בפריט הראשון למכירה)
    public DateTime? SaleCommissionConsentAtUtc { get; set; }

    // תיעוד אישור תנאי השימוש בהרשמה (מועד וגרסה). null = חשבון שנוצר לפני תיעוד ההסכמה
    public DateTime? TermsAcceptedAtUtc { get; set; }
    public string? TermsVersion { get; set; }

    // חיוב אוטומטי חודשי של עמלת השירות בכרטיס אשראי שמור (סאמיט): מתי אושרה ההרשאה, והאם הכרטיס נשמר בפועל (אחרי התשלום הראשון)
    public DateTime? AutoChargeConsentAtUtc { get; set; }
    public bool AutoChargeEnabled { get; set; }

    public bool ShippingCardSaved { get; set; }

    // "אין לחייג אלי בלילה" - ראו QuietHours
    public bool QuietHoursEnabled { get; set; }
    public string? QuietFrom { get; set; }
    public string? QuietTo { get; set; }
    public DateTime? ShippingCardConsentAtUtc { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    // הערות על הכתובת (אופציונלי) - למשל "כניסה מהחצר, קומה 2, לצלצל פעמיים". מוצגות לשוכרת באיסוף עצמי
    // ומועברות לשליח במשלוח (ראו LoanService / GetPackageShippingService)
    public string? AddressNotes { get; set; }

    public string? AddressFloor { get; set; }

    // שם הרחוב בלבד (בלי מספר בית) - כפי שנבחר בשדה הרחוב הנפרד
    public string? AddressStreet { get; set; }
    public string? AddressApartment { get; set; }

    // פרטי חשבון בנק לקבלת תשלומים עבור השכרות (נאספים מהמשכיר מיד לאחר הגדרת כתובת המגורים -
    // ראו UserService.UpdatePayoutAsync). כל עוד PayoutAccountNumber ריק, המשכיר לא הגדיר עדיין
    // אמצעי לקבלת תשלומים, ולכן לא ניתן להשלים עסקת השכרה בתשלום בהעברה בנקאית עבור פריטיו.
    public string? PayoutAccountHolderName { get; set; }

    public string? PayoutBankName { get; set; }

    public string? PayoutBranchNumber { get; set; }

    // נשמר במסד מוצפן (AES-GCM, ר' FieldEncryptor) - UserRepository מצפין בכתיבה ומפענח בקריאה,
    // כך שבכל שאר הקוד הערך כאן הוא תמיד מספר החשבון הגלוי.
    public string? PayoutAccountNumber { get; set; }

    // מתי המשכיר אישר (בתיבת הסימון בטופס) שפרטי החשבון יישמרו ויוצגו לשוכר בתשלום בהעברה
    // בנקאית - תיעוד ההסכמה כנדרש לפי חוק הגנת הפרטיות. מתעדכן בכל שמירה של פרטי החשבון.
    public DateTime? PayoutConsentAtUtc { get; set; }

    // אסימון איפוס סיסמה חד-פעמי (ראו AuthService.RequestPasswordResetAsync) - null כאשר אין בקשת
    // איפוס פעילה. מתבטל אוטומטית לאחר שימוש או פקיעת תוקף (PasswordResetTokenExpiresUtc).
    public string? PasswordResetToken { get; set; }

    public DateTime? PasswordResetTokenExpiresUtc { get; set; }
}
