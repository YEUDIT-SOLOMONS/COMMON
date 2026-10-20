using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// חלון "זמין / להסרה מהאתר" - הקישורים שנשלחים לבעלת הפריט במייל כשהפריט לא הושכר 6 שבועות. נפתחים בלי
// התחברות ומוגנים בטוקן סודי ארוך שייחודי לפריט (?t=...). טוקן שגוי = 404.
[ApiController]
[Route("api/item-actions")]
[AllowAnonymous]
public class ItemActionsController : ControllerBase
{
    private readonly IStaleItemService _staleItemService;

    public ItemActionsController(IStaleItemService staleItemService)
    {
        _staleItemService = staleItemService;
    }

    [HttpGet("{itemId}")]
    public async Task<IActionResult> Get([FromRoute] string itemId, [FromQuery(Name = "t")] string? token)
    {
        if (!await _staleItemService.ValidateTokenAsync(itemId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        var view = await _staleItemService.GetActionViewAsync(itemId);
        return view is null ? NotFound(new { Message = "הפריט לא נמצא" }) : Ok(view);
    }

    [HttpPost("{itemId}/availability-confirmation")]
    public async Task<IActionResult> Available([FromRoute] string itemId, [FromQuery(Name = "t")] string? token)
    {
        if (!await _staleItemService.ValidateTokenAsync(itemId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        await _staleItemService.ConfirmAvailableAsync(itemId);
        return NoContent();
    }

    [HttpPost("{itemId}/removal")]
    public async Task<IActionResult> Remove([FromRoute] string itemId, [FromQuery(Name = "t")] string? token)
    {
        if (!await _staleItemService.ValidateTokenAsync(itemId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        try
        {
            await _staleItemService.RemoveAsync(itemId);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }
}
