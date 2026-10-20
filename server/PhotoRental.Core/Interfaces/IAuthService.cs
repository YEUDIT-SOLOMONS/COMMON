namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;

// שכבת הלוגיקה העסקית לאימות משתמשים (הרשמה/התחברות).
// אינה עוסקת בהנפקת טוקן JWT בעצמה - זו אחריות שכבת ה-API (המארחת את האפליקציה),
// כדי לא לצרף תלות בחבילות ASP.NET Authentication לשכבת השירות (Service) הטהורה.
public interface IAuthService
{
    Task<User> RegisterAsync(RegisterDto dto);
    Task<User> LoginAsync(LoginDto dto);

    // התחברות/הרשמה אוטומטית באמצעות Google - ראו הערה מפורטת ב-AuthService.LoginWithGoogleAsync
    Task<User> LoginWithGoogleAsync(GoogleLoginDto dto);
    Task<ForgotPasswordResponseDto> RequestPasswordResetAsync(ForgotPasswordDto dto);
    Task ResetPasswordAsync(ResetPasswordDto dto);
}
