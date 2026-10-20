namespace PhotoRental.Core.DTOs;

// נשלח מהלקוח בעת הרשמה למערכת (יצירת משתמש חדש + הגדרת סיסמה).
// אין כאן בחירת תפקיד (משכיר/שוכר) - כל חשבון יכול גם לשכור וגם לשתף ציוד משלו,
// ראו AuthService.RegisterAsync.
public class RegisterDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool TermsAccepted { get; set; }
    public bool QuietHoursEnabled { get; set; }
    public string? QuietFrom { get; set; }
    public string? QuietTo { get; set; }
}

// נשלח מהלקוח בעת התחברות למערכת
public class LoginDto
{
    public string Email { get; set; } = string.Empty;
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
    public string IdToken { get; set; } = string.Empty;
}

// נשלח מהלקוח כדי לבקש איפוס סיסמה
public class ForgotPasswordDto
{
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
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
