using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ItemsController : ControllerBase
{
    private readonly IItemService _itemService;

    private readonly IItemScheduleService _schedule;

    public ItemsController(IItemService itemService, IItemScheduleService schedule)
    {
        _itemService = itemService;
        _schedule = schedule;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin => User.IsInRole("Admin");

    // שליפת כל הציוד, עם אפשרות סינון לפי קטגוריה (?categoryId=...) או לפי בעלים (?ownerId=...) - לשימוש "האזור האישי"
    // לוח הזמנים של הפריט: לכולן - טווחים תפוסים; למשכירה - גם ההשכרות והימים שחסמה
    [HttpGet("{id}/schedule")]
    public async Task<IActionResult> Schedule([FromRoute] string id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Ok(await _schedule.GetScheduleAsync(id, userId));
    }

    [Authorize]
    [HttpPost("{id}/blocks")]
    public async Task<IActionResult> AddBlock([FromRoute] string id, [FromBody] ItemBlockCreateDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _schedule.AddBlockAsync(id, userId, dto));
    }

    [Authorize]
    [HttpDelete("{id}/blocks/{blockId}")]
    public async Task<IActionResult> DeleteBlock([FromRoute] string id, [FromRoute] string blockId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await _schedule.DeleteBlockAsync(id, blockId, userId);
        return NoContent();
    }

    // "פנוי בתאריכים שלי" בקטלוג: ?from=2026-11-10&to=2026-11-12 -> מזהי הפריטים התפוסים בטווח
    [HttpGet("busy")]
    public async Task<IActionResult> Busy([FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        if (to < from || (to - from).TotalDays > 365)
            return BadRequest(new { Message = "טווח התאריכים אינו תקין" });
        var key = $"items:busy:{from:yyyyMMdd}:{to:yyyyMMdd}";
        return Ok(await CatalogCache.GetOrAddAsync(key, TimeSpan.FromSeconds(60), () => _itemService.GetBusyItemIdsAsync(from, to)));
    }

    // GET /api/items/page?page=2&pageSize=12&categoryId=...&sort=price - דפדוף בצד בסיס הנתונים
    [HttpGet("page")]
    public async Task<ActionResult<PagedResultDto<ItemDto>>> GetPage([FromQuery] ItemPageQueryDto query, CancellationToken ct)
    {
        return Ok(await _itemService.GetPageAsync(query, ct));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? categoryId, [FromQuery] string? ownerId)
    {
        var ttl = TimeSpan.FromSeconds(60);
        if (!string.IsNullOrWhiteSpace(ownerId))
        {
            return Ok(await CatalogCache.GetOrAddAsync($"items:owner:{ownerId}", ttl, () => _itemService.GetByOwnerAsync(ownerId)));
        }

        var items = string.IsNullOrWhiteSpace(categoryId)
            ? await CatalogCache.GetOrAddAsync("items:all", ttl, () => _itemService.GetAllAsync())
            : await CatalogCache.GetOrAddAsync($"items:cat:{categoryId}", ttl, () => _itemService.GetByCategoryAsync(categoryId));

        return Ok(items);
    }

    // שליפת פריט לפי ID
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id, CancellationToken ct)
    {
        var item = await _itemService.GetByIdAsync(id, ct);
        return item is null ? NotFound(new { Message = "הפריט לא נמצא" }) : Ok(item);
    }

    // הכתובת המדויקת של הפריט - נחשפת רק בקריאה מפורשת ונפרדת זו, לשימוש הלקוח רק לאחר
    // שהשוכר בוחר באיסוף עצמי בתהליך ההשכרה (ראו ItemAddressDto)
    [Authorize]
    [HttpGet("{id}/address")]
    public async Task<IActionResult> GetAddress([FromRoute] string id)
    {
        var address = await _itemService.GetAddressAsync(id);
        return Ok(address);
    }

    // פרטי חשבון הבנק של בעל הפריט - לשימוש הלקוח כאשר השוכר בוחר לשלם בהעברה בנקאית
    [Authorize]
    [HttpGet("{id}/owner-payout")]
    public async Task<IActionResult> GetOwnerPayout([FromRoute] string id)
    {
        var payout = await _itemService.GetOwnerPayoutAsync(id);
        return Ok(payout);
    }

    // הוספת ציוד חדש
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] ItemCreateDto newItem)
    {
        // פריט נוצר רק על שם המשתמשת המחוברת (או ע"י מנהלת)
        if (!IsAdmin && newItem.OwnerId != CurrentUserId) return Forbid();

        var created = await _itemService.CreateAsync(newItem);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // עריכת פריט קיים (רק בעליו)
    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] ItemUpdateDto dto)
    {
        // השרת משווה את dto.OwnerId לבעלת הפריט - כאן מחליפים אותו בזהות המחוברת כדי שלא ניתן לזייף
        if (!IsAdmin) dto.OwnerId = CurrentUserId;

        var updated = await _itemService.UpdateAsync(id, dto);
        return Ok(updated);
    }

    // מחיקת ציוד
    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> Delete([FromRoute] string id)
    {
        var item = await _itemService.GetByIdAsync(id);
        if (item is null) return NotFound(new { Message = "הפריט לא נמצא" });
        if (!IsAdmin && item.OwnerId != CurrentUserId) return Forbid();

        await _itemService.DeleteAsync(id);
        return NoContent();
    }
}
