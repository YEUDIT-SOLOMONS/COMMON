using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.API.Security;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public AuthController(IAuthService authService, IJwtTokenGenerator jwtTokenGenerator)
    {
        _authService = authService;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    // הרשמת משתמש חדש (שוכר/משכיר) + החזרת טוקן התחברות מיידי, כדי שהלקוח יהיה מחובר מייד לאחר ההרשמה
    [EnableRateLimiting("signup")]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var user = await _authService.RegisterAsync(dto);
        return Ok(BuildResponse(user));
    }

    // התחברות משתמש קיים באמצעות אימייל וסיסמה
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _authService.LoginAsync(dto);
        return Ok(BuildResponse(user));
    }

    // התחברות (או הרשמה אוטומטית) באמצעות Google דרך Firebase Authentication - ראו הערה מפורטת
    // ב-AuthService.LoginWithGoogleAsync
    [EnableRateLimiting("login")]
    [HttpPost("google")]
    public async Task<IActionResult> Google([FromBody] GoogleLoginDto dto)
    {
        var user = await _authService.LoginWithGoogleAsync(dto);
        return Ok(BuildResponse(user));
    }

    // בקשת איפוס סיסמה - מחזיר אסימון איפוס (אין שירות דוא"ל אמיתי באתר, ראו הערה ב-ForgotPasswordResponseDto)
    [EnableRateLimiting("signup")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var result = await _authService.RequestPasswordResetAsync(dto);
        return Ok(result);
    }

    // השלמת איפוס הסיסמה בעזרת אסימון האיפוס
    [EnableRateLimiting("signup")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        await _authService.ResetPasswordAsync(dto);
        return Ok(new { Message = "הסיסמה עודכנה בהצלחה" });
    }

    private AuthResponseDto BuildResponse(User user)
    {
        var (token, expiresAtUtc) = _jwtTokenGenerator.GenerateToken(user);

        return new AuthResponseDto
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            User = new UserDto
            {
                Id = user.Id ?? string.Empty,
                Name = user.Name,
                Email = user.Email,
                Phone = user.Phone,
                Role = user.Role.ToString(),
                Address = user.Address,
                City = user.City,
                AddressNotes = user.AddressNotes,
                AddressFloor = user.AddressFloor,
                AddressApartment = user.AddressApartment,
                QuietHoursEnabled = user.QuietHoursEnabled,
                QuietFrom = user.QuietFrom ?? QuietHours.DefaultFrom,
                QuietTo = user.QuietTo ?? QuietHours.DefaultTo
            }
        };
    }
}
