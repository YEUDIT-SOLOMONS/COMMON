using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.API.BackgroundServices;

namespace PhotoRental.API.Controllers;

// נקודת הפעלה למשימות מתוזמנות (תזכורות החזרה, "הפריט נאסף?", דוח עמלה חודשי). נקראת כל 5 דקות מפונקציית ענן
// של Firebase (ראו תיקיית scheduler) ומעירה את השרת, כך שהמשימות רצות בזמן גם בלי אינסטנציה פעילה תמיד.
// מוגנת במפתח סודי משותף בכותרת X-Scheduler-Key (Scheduler:Key, מגיע מ-Secret Manager). בלי מפתח מוגדר בשרת -
// הנקודה סגורה לגמרי.
[ApiController]
[Route("api/internal")]
[AllowAnonymous]
public class SchedulerController : ControllerBase
{
    private readonly ReturnReminderBackgroundService _reminders;
    private readonly MonthlyCommissionBackgroundService _monthly;
    private readonly StaleItemBackgroundService _staleItems;
    private readonly string _key;

    public SchedulerController(
        ReturnReminderBackgroundService reminders,
        MonthlyCommissionBackgroundService monthly,
        StaleItemBackgroundService staleItems,
        IConfiguration configuration)
    {
        _reminders = reminders;
        _monthly = monthly;
        _staleItems = staleItems;
        _key = configuration["Scheduler:Key"] ?? string.Empty;
    }

    [HttpPost("scheduled-runs")]
    public async Task<IActionResult> RunScheduled([FromServices] PhotoRental.Core.Interfaces.ISaleService sales)
    {
        var provided = Request.Headers["X-Scheduler-Key"].ToString();
        if (string.IsNullOrEmpty(_key) || !FixedTimeEquals(provided, _key))
            return NotFound();

        await _reminders.RunRoundAsync();
        await _monthly.SendPreviousMonthAsync();
        await _staleItems.RunAsync();
        try { await sales.AutoConfirmAsync(); } catch { }
        return Ok(new { ok = true, atUtc = DateTime.UtcNow });
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var x = SHA256.HashData(Encoding.UTF8.GetBytes(a));
        var y = SHA256.HashData(Encoding.UTF8.GetBytes(b));
        return CryptographicOperations.FixedTimeEquals(x, y);
    }
}
