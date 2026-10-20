using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// נתוני מעקב ומספרים כלליים למסך הניהול - למנהלות בלבד (תפקיד Admin)
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ICallLogRepository _callLogs;
    private readonly IPayoutService _payouts;

    public AdminController(IAdminService adminService, ICallLogRepository callLogs, IPayoutService payouts)
    {
        _adminService = adminService;
        _callLogs = callLogs;
        _payouts = payouts;
    }

    [HttpGet("payouts")]
    public async Task<IActionResult> GetPayouts() => Ok(await _payouts.GetAsync());

    [HttpGet("payouts/{loanId}/proof")]
    public async Task<IActionResult> GetPayoutProof([FromRoute] string loanId, [FromServices] ILoanService loans)
    {
        var proof = await loans.GetPayoutProofAsync(loanId);
        return proof is null ? NotFound(new { Message = "לא הועלתה אסמכתא" }) : Ok(proof);
    }

    // כל הרחובות שיש בהם פריטים, והשכונה שנקבעה לכל רחוב (מוצגת בחלון הפריט במקום הרחוב)
    [HttpGet("neighborhoods")]
    public async Task<IActionResult> GetNeighborhoods([FromServices] IItemRepository items, [FromServices] IStreetNeighborhoodRepository streets)
    {
        var map = await streets.GetAllAsync();
        var rows = (await items.GetAllAsync())
            .Where(i => i.DeletedAtUtc is null && !string.IsNullOrWhiteSpace(i.City))
            .Select(i => (City: i.City!.Trim(), Street: PhotoRental.Service.Services.ItemService.StreetOf(i)?.Trim()))
            .Where(r => !string.IsNullOrEmpty(r.Street))
            .GroupBy(r => r)
            .Select(g => new
            {
                g.Key.City,
                g.Key.Street,
                Items = g.Count(),
                Neighborhood = map.TryGetValue(PhotoRental.Core.Helpers.KnownNeighborhoods.Key(g.Key.City, g.Key.Street), out var n) ? n : null
            })
            .OrderBy(r => r.Neighborhood is null ? 0 : 1).ThenBy(r => r.City).ThenBy(r => r.Street)
            .ToList();
        return Ok(new { Rows = rows, Options = PhotoRental.Core.Helpers.KnownNeighborhoods.ByCity });
    }

    public class StreetNeighborhoodDto
    {
        public string City { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string? Neighborhood { get; set; }
    }

    [HttpPut("neighborhoods")]
    public async Task<IActionResult> SetNeighborhood([FromBody] StreetNeighborhoodDto dto, [FromServices] IStreetNeighborhoodRepository streets)
    {
        if (string.IsNullOrWhiteSpace(dto.City) || string.IsNullOrWhiteSpace(dto.Street))
            return BadRequest(new { Message = "חסרים עיר ורחוב" });
        if (dto.Neighborhood is { Length: > 60 })
            return BadRequest(new { Message = "שם השכונה ארוך מדי" });
        await streets.SetAsync(dto.City, dto.Street, dto.Neighborhood);
        return NoContent();
    }

    // כל הפריטים לשיוך מחדש לקטגוריה
    [HttpGet("items")]
    public async Task<IActionResult> GetItems([FromServices] IItemRepository items, [FromServices] IUserRepository users)
    {
        var userMap = (await users.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);
        var rows = (await items.GetAllAsync())
            .Where(i => i.DeletedAtUtc is null && !string.IsNullOrEmpty(i.Id))
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new
            {
                i.Id,
                i.Name,
                OwnerName = !string.IsNullOrEmpty(i.OwnerId) && userMap.TryGetValue(i.OwnerId, out var o) ? o.Name : string.Empty,
                i.CategoryId,
                i.ExtraCategoryId,
                Image = PhotoRental.Core.Helpers.ImageLinks.Thumb(i.Id, i.ImageDataUrl),
                i.CreatedAtUtc
            })
            .ToList();
        return Ok(rows);
    }

    public class SetItemCategoryDto
    {
        public string CategoryId { get; set; } = string.Empty;
        public string? ExtraCategoryId { get; set; }
    }

    [HttpPut("items/{id}/category")]
    public async Task<IActionResult> SetItemCategory([FromRoute] string id, [FromBody] SetItemCategoryDto dto, [FromServices] IItemService itemService)
    {
        await itemService.SetCategoryAsync(id, dto.CategoryId, dto.ExtraCategoryId);
        return NoContent();
    }

    // כל ההתכתבויות בין שוכרות למשכירות (שאלות על פריטים)
    [HttpGet("questions")]
    public async Task<IActionResult> GetQuestions(
        [FromServices] IItemQuestionRepository questions, [FromServices] IItemRepository items, [FromServices] IUserRepository users)
    {
        var itemMap = (await items.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var userMap = (await users.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);
        string Name(string id) => userMap.TryGetValue(id, out var u) ? u.Name : "(משתמשת שנמחקה)";
        string Email(string id) => userMap.TryGetValue(id, out var u) ? u.Email : string.Empty;
        var rows = (await questions.GetAllAsync())
            .OrderByDescending(q => q.LastMessageAtUtc)
            .Select(q => new
            {
                q.Id,
                q.ItemId,
                ItemName = itemMap.TryGetValue(q.ItemId, out var i) ? i.Name : "(פריט שנמחק)",
                OwnerName = Name(q.OwnerId),
                OwnerEmail = Email(q.OwnerId),
                RenterName = Name(q.RenterId),
                RenterEmail = Email(q.RenterId),
                q.LastMessageAtUtc,
                q.OwnerToken,
                Messages = q.Messages.Select(m => new { m.From, m.Text, m.AtUtc, m.ImageIds })
            })
            .ToList();
        return Ok(rows);
    }

    public class EditMessageDto
    {
        public string Text { get; set; } = string.Empty;
    }

    // עריכת הודעה בהתכתבות (למשל פרטי קשר שנכתבו). טקסט ריק - מחיקת ההודעה
    [HttpPut("questions/{id}/messages/{index:int}")]
    public async Task<IActionResult> EditQuestionMessage([FromRoute] string id, [FromRoute] int index, [FromBody] EditMessageDto dto, [FromServices] IItemQuestionRepository questions)
    {
        var thread = await questions.GetByIdAsync(id);
        if (thread is null || index < 0 || index >= thread.Messages.Count)
            return NotFound(new { Message = "ההודעה לא נמצאה" });
        var text = (dto.Text ?? string.Empty).Trim();
        if (text.Length == 0)
            thread.Messages.RemoveAt(index);
        else
            thread.Messages[index].Text = text.Length > 1000 ? text[..1000] : text;
        await questions.UpdateAsync(thread);
        return NoContent();
    }

    [HttpGet("loans")]
    public async Task<IActionResult> GetLoans(
        [FromServices] ILoanRepository loans, [FromServices] IItemRepository items, [FromServices] IUserRepository users)
    {
        var itemMap = (await items.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var userMap = (await users.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);
        string NameOf(string? id) => !string.IsNullOrEmpty(id) && userMap.TryGetValue(id, out var u) ? u.Name : string.Empty;

        var rows = (await loans.GetAllAsync())
            .OrderByDescending(l => l.LoanDate)
            .Select(l =>
            {
                itemMap.TryGetValue(l.ItemCopyId ?? string.Empty, out var item);
                var renter = !string.IsNullOrEmpty(l.BorrowerId) && userMap.TryGetValue(l.BorrowerId, out var r) ? r : null;
                return new PhotoRental.Core.DTOs.AdminLoanDto
                {
                    Id = l.Id ?? string.Empty,
                    ItemName = item is null ? "(פריט שנמחק)" : PhotoRental.Core.Helpers.LoanText.Name(item, l),
                    RenterName = renter?.Name ?? "(משתמשת שנמחקה)",
                    RenterEmail = renter?.Email ?? string.Empty,
                    OwnerName = NameOf(item?.OwnerId),
                    CreatedAt = l.LoanDate,
                    StartDate = l.StartDate,
                    StartTime = l.StartTime,
                    ExpectedReturnDate = DateTime.SpecifyKind(l.ExpectedReturnDate, DateTimeKind.Unspecified),
                    ActualReturnDate = l.ActualReturnDate,
                    Status = l.Status.ToString(),
                    PaymentMethod = l.PaymentMethod.ToString(),
                    PaymentStatus = l.PaymentStatus.ToString(),
                    DeliveryMethod = l.DeliveryMethod.ToString(),
                    Total = (l.RentalDays + l.LateDays) * l.RateOr(item?.DailyPrice ?? 0) * l.Units + l.DeliveryFee + l.ReturnFee
                };
            })
            .ToList();
        return Ok(rows);
    }

    // מחיקת השכרה לצמיתות (למשל השכרת ניסיון). לוח הזמנים ומצב הפריט מתעדכנים
    [HttpDelete("loans/{id}")]
    public async Task<IActionResult> DeleteLoan([FromRoute] string id, [FromServices] ILoanRepository loans, [FromServices] IItemScheduleService schedule)
    {
        var loan = await loans.GetByIdAsync(id);
        if (loan is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        await loans.DeleteAsync(id);
        PhotoRental.Core.Helpers.CatalogCache.Invalidate();
        if (!string.IsNullOrEmpty(loan.ItemCopyId))
        {
            try { await schedule.RefreshStatusAsync(loan.ItemCopyId); } catch { }
        }
        return NoContent();
    }

    [HttpGet("favorites")]
    public async Task<IActionResult> GetFavorites(
        [FromServices] IFavoriteRepository favorites, [FromServices] IItemRepository items, [FromServices] IUserRepository users)
    {
        var all = (await favorites.GetAllAsync()).Where(f => f.IsFavorite || f.NotifyWhenAvailable).ToList();
        var itemMap = (await items.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var userMap = (await users.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);

        var rows = all.GroupBy(f => f.ItemId)
            .Where(g => itemMap.ContainsKey(g.Key) && itemMap[g.Key].DeletedAtUtc is null)
            .Select(g =>
            {
                var item = itemMap[g.Key];
                return new PhotoRental.Core.DTOs.AdminFavoriteDto
                {
                    ItemId = g.Key,
                    ItemName = item.Name,
                    OwnerName = !string.IsNullOrEmpty(item.OwnerId) && userMap.TryGetValue(item.OwnerId, out var owner) ? owner.Name : string.Empty,
                    Status = item.Status.ToString(),
                    Favorites = g.Count(f => f.IsFavorite),
                    Waiting = g.Count(f => f.NotifyWhenAvailable),
                    LastSavedUtc = g.Max(f => f.CreatedAtUtc),
                    Savers = g.OrderByDescending(f => f.CreatedAtUtc)
                        .Select(f => userMap.TryGetValue(f.UserId, out var u)
                            ? new PhotoRental.Core.DTOs.AdminFavoriteSaverDto { Name = u.Name, Email = u.Email, Waiting = f.NotifyWhenAvailable }
                            : new PhotoRental.Core.DTOs.AdminFavoriteSaverDto { Name = "(משתמשת שנמחקה)", Waiting = f.NotifyWhenAvailable })
                        .ToList()
                };
            })
            .OrderByDescending(r => r.Favorites + r.Waiting)
            .ThenByDescending(r => r.LastSavedUtc)
            .ToList();
        return Ok(rows);
    }

    [HttpGet("visits")]
    public async Task<IActionResult> GetVisits([FromServices] IVisitRepository visits) => Ok(await visits.GetAllAsync());

    [HttpPost("payouts/payments")]
    public async Task<IActionResult> MarkPayoutPaid([FromBody] MarkPayoutPaidDto dto)
    {
        await _payouts.MarkPaidAsync(dto);
        return NoContent();
    }

    // יומן שיחות הטלפון האוטומטיות (החדשות ראשונות, עד 300)
    [HttpGet("calls")]
    public async Task<IActionResult> GetCalls()
    {
        var logs = await _callLogs.GetAllAsync();
        var rows = logs
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(300)
            .Select(l => new CallLogDto
            {
                Id = l.Id ?? string.Empty,
                Kind = l.Kind,
                ItemName = l.ItemName,
                ToName = l.ToName,
                ToPhone = l.ToPhone,
                InterestedEmail = l.InterestedEmail,
                CreatedAtUtc = l.CreatedAtUtc,
                Status = l.Status,
                ScheduledForLocal = l.ScheduledForLocal is null ? null : DateTime.SpecifyKind(l.ScheduledForLocal.Value, DateTimeKind.Unspecified),
                Detail = l.Detail,
                Message = l.Message
            })
            .ToList();
        return Ok(rows);
    }

    // שליחה חוזרת של שיחת היכרות: נוצרת שיחה מתוזמנת חדשה עם אותו נוסח (בשבת/חג - נדחית למוצאי השבת/החג).
    // תצא בסבב הבא של המתזמן (תוך כמה דקות)
    [HttpPost("calls/{id}/resends")]
    public async Task<IActionResult> ResendCall([FromRoute] string id)
    {
        var original = await _callLogs.GetByIdAsync(id);
        if (original is null)
            return NotFound(new { Message = "השיחה לא נמצאה" });
        if (original.Kind != "Welcome")
            return BadRequest(new { Message = "אפשר לשלוח שוב רק שיחת היכרות" });

        var phoneKey = new string((original.ToPhone ?? string.Empty).Where(char.IsDigit).ToArray());
        if ((await _callLogs.GetByStatusAsync("Scheduled")).Any(l => l.Kind == "Welcome" &&
                new string((l.ToPhone ?? string.Empty).Where(char.IsDigit).ToArray()) == phoneKey))
            return Conflict(new { Message = "כבר יש שיחת היכרות מתוזמנת למספר הזה" });

        var nowLocal = PhotoRental.Core.Helpers.IsraelTime.Now;
        var next = PhotoRental.Core.Helpers.CallWindow.NextAllowed(nowLocal);
        var created = await _callLogs.CreateAsync(new PhotoRental.Core.Entities.CallLog
        {
            Kind = original.Kind,
            ToUserId = original.ToUserId,
            ToName = original.ToName,
            ToPhone = original.ToPhone,
            Message = original.Message,
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Scheduled",
            ScheduledForLocal = next > nowLocal ? next : nowLocal.AddMinutes(-1)
        });
        return Ok(new { created.Id, ScheduledForLocal = next > nowLocal ? next : (DateTime?)null });
    }

    public class AvailabilityResendDto
    {
        public bool Call { get; set; }
        public bool Email { get; set; }
    }

    // אימות זמינות שלא נענה: שיחה חוזרת (מכבדת שעות שקטות) ו/או מייל למשכירה עם כפתור תשובה
    [HttpPost("calls/{id}/availability-resends")]
    public async Task<IActionResult> ResendAvailability([FromRoute] string id, [FromBody] AvailabilityResendDto dto,
        [FromServices] IAvailabilityCallService availability)
    {
        try
        {
            var (callAt, emailed) = await availability.AdminResendAsync(id, dto.Call, dto.Email);
            return Ok(new { CallAtLocal = callAt, Emailed = emailed });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { Message = ex.Message });
        }
    }

    // ביטול שיחה שעוד לא יצאה
    [HttpPost("calls/{id}/cancellation")]
    public async Task<IActionResult> CancelCall([FromRoute] string id)
    {
        var log = await _callLogs.GetByIdAsync(id);
        if (log is null)
            return NotFound(new { Message = "השיחה לא נמצאה" });
        if (log.Status is not ("Scheduled" or "RetryScheduled"))
            return BadRequest(new { Message = "אפשר לבטל רק שיחה שעוד לא יצאה" });
        log.Status = "Canceled";
        await _callLogs.UpdateAsync(log);
        return NoContent();
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        return Ok(await _adminService.GetOverviewAsync());
    }
}
