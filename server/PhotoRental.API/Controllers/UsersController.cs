using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// כל הפעולות כאן דורשות משתמש מחובר (טוקן JWT). פעולות על משתמש מסוים ({id}) מותרות רק למשתמש
// עצמו או למנהל/ת מערכת - אחרת כל אחד היה יכול לקרוא, ובעיקר לשנות, פרטי חשבון בנק של משכיר
// אחר (ולהסיט אליו תשלומים). רשימת כל המשתמשים ויצירת משתמש ישירות - למנהלים בלבד (הרשמה
// רגילה עוברת דרך AuthController).
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    private bool IsSelfOrAdmin(string id) =>
        User.IsInRole("Admin") || User.FindFirstValue(ClaimTypes.NameIdentifier) == id;

    // שליפת כל המשתמשים
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        var users = await _userService.GetAllAsync();
        return Ok(users);
    }

    // שליפת משתמש לפי ID
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        var user = await _userService.GetByIdAsync(id);
        return user is null ? NotFound(new { Message = "משתמש לא נמצא" }) : Ok(user);
    }

    // יצירת משתמש חדש
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] UserCreateDto newUser)
    {
        var created = await _userService.CreateAsync(newUser);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // עדכון כתובת מגורים ועיר עבור משתמש (משכיר) - נדרש לפני הוספת הפריט הראשון להשכרה,
    // ומשמש למילוי אוטומטי של מיקום הפריטים שהוא מוסיף מכאן ואילך. ניתן לקרוא גם בהמשך כדי
    // לעדכן כתובת קיימת מתוך האזור האישי.
    [HttpPatch("{id}/address")]
    public async Task<IActionResult> UpdateAddress([FromRoute] string id, [FromBody] UserAddressUpdateDto request)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        var updated = await _userService.UpdateAddressAsync(id, request);
        return Ok(updated);
    }

    // עדכון פרטים אישיים (שם/טלפון) מהאזור האישי, בכל עת
    [HttpPatch("{id}/profile")]
    public async Task<IActionResult> UpdateProfile([FromRoute] string id, [FromBody] UserProfileUpdateDto request)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        var updated = await _userService.UpdateProfileAsync(id, request);
        return Ok(updated);
    }

    // אישור תנאי עמלת השירות (פעם אחת, בעת הוספת הפריט הראשון) - נשמר עם חותמת זמן
    [HttpPatch("{id}/commission-consent")]
    public async Task<IActionResult> AcceptCommission([FromRoute] string id)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        return Ok(await _userService.AcceptCommissionAsync(id));
    }

    [HttpPatch("{id}/sale-commission-consent")]
    public async Task<IActionResult> AcceptSaleCommission([FromRoute] string id)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        return Ok(await _userService.AcceptSaleCommissionAsync(id));
    }

    // שליפת פרטי חשבון הבנק של המשתמש לקבלת תשלומים (לתצוגה/עריכה באזור האישי)
    [HttpGet("{id}/payout")]
    public async Task<IActionResult> GetPayout([FromRoute] string id)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        var payout = await _userService.GetPayoutAsync(id);
        return Ok(payout);
    }

    // הגדרה/עדכון פרטי חשבון הבנק - ניתן לשינוי בכל עת מהאזור האישי
    [HttpPatch("{id}/payout")]
    public async Task<IActionResult> UpdatePayout([FromRoute] string id, [FromBody] UserPayoutUpdateDto request)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        var updated = await _userService.UpdatePayoutAsync(id, request);
        return Ok(updated);
    }

    // מחיקת משתמש
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete([FromRoute] string id)
    {
        if (!IsSelfOrAdmin(id)) return Forbid();
        await _userService.DeleteAsync(id);
        return NoContent();
    }
}
