namespace PhotoRental.Core.DTOs;

using System.ComponentModel.DataAnnotations;

// נשלח מהלקוח בעת הרשמה למערכת (יצירת משתמש חדש + הגדרת סיסמה).
// אין כאן בחירת תפקיד (משכיר/שוכר) - כל חשבון יכול גם לשכור וגם לשתף ציוד משלו,
// ראו AuthService.RegisterAsync.
public class RegisterDto
{
    [Required(ErrorMessage = "יש להזין שם מלא")]
    [StringLength(80, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;
    [Required(ErrorMessage = "יש להזין כתובת אימייל")]
    [EmailAddress(ErrorMessage = "כתובת האימייל אינה תקינה")]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;
    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;
    [Required]
    [StringLength(128, MinimumLength = 6, ErrorMessage = "הסיסמה צריכה לכלול לפחות 6 תווים")]
    public string Password { get; set; } = string.Empty;
    public bool TermsAccepted { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public string? QuietFrom { get; set; }
    public string? QuietTo { get; set; }
}

// נשלח מהלקוח בעת התחברות למערכת
public class LoginDto
{
    [Required(ErrorMessage = "יש להזין אימייל")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required(ErrorMessage = "יש להזין סיסמה")]
    public string Password { get; set; } = string.Empty;
}

// מוחזר ללקוח לאחר הרשמה/התחברות מוצלחת - כולל טוקן ה-JWT שיש לצרף לבקשות הבאות
// (בכותרת Authorization: Bearer {Token})
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public UserDto User { get; set; } = new();
}

// נשלח מהלקוח לאחר התחברות מוצלחת מול Google דרך Firebase Authentication (ראו firebase.js בצד
// הלקוח) - כולל אסימון הזהות (ID token, JWT חתום ע"י שירות "securetoken" של Firebase) שהתקבל שם.
// השרת הוא שמאמת את האסימון מול גוגל (ולא הלקוח), כדי שלא ניתן יהיה לזייף התחברות ע"י שליחת
// אימייל בלבד - ראו AuthService.LoginWithGoogleAsync.
public class GoogleLoginDto
{
    [Required]
    public string IdToken { get; set; } = string.Empty;
}

// נשלח מהלקוח כדי לבקש איפוס סיסמה
public class ForgotPasswordDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

// מוחזר ללקוח לאחר בקשת איפוס סיסמה. אין באתר שירות דוא"ל אמיתי (בדומה לשיחת אימות הזמינות
// המדומה) - לכן, כדי שהתכונה תישאר שמישה להדגמה, אסימון האיפוס מוחזר ישירות כאן (הלקוח בונה
// ממנו את קישור האיפוס המלא) ולא נשלח במייל אמיתי. ResetToken הוא null אם כתובת המייל אינה
// קיימת במערכת (כדי לא לחשוף אילו כתובות רשומות ומי לא).
public class ForgotPasswordResponseDto
{
    public string Message { get; set; } = string.Empty;
    public string? ResetToken { get; set; }
}

// נשלח מהלקוח כדי להשלים איפוס סיסמה בעזרת האסימון שהתקבל מ-ForgotPasswordResponseDto
public class ResetPasswordDto
{
    [Required]
    public string Token { get; set; } = string.Empty;
    [Required]
    [StringLength(128, MinimumLength = 6)]
    public string NewPassword { get; set; } = string.Empty;
}
