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
    private const int CallLogLimit = 300;
    private readonly IAdminService _admin;
    private readonly ICallLogService _calls;
    private readonly IPayoutService _payouts;
    private readonly IQuestionService _questions;
    private readonly IVisitService _visits;

    public AdminController(IAdminService admin, ICallLogService calls, IPayoutService payouts, IQuestionService questions, IVisitService visits)
    {
        _admin = admin;
        _calls = calls;
        _payouts = payouts;
        _questions = questions;
        _visits = visits;
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview() => Ok(await _admin.GetOverviewAsync());

    [HttpGet("payouts")]
    public async Task<IActionResult> GetPayouts() => Ok(await _payouts.GetAsync());

    [HttpGet("payouts/{loanId}/proof")]
    public async Task<IActionResult> GetPayoutProof([FromRoute] string loanId, [FromServices] ILoanService loans)
    {
        var proof = await loans.GetPayoutProofAsync(loanId);
        return proof is null ? NotFound(new { Message = "לא הועלתה אסמכתא" }) : Ok(proof);
    }

    [HttpPost("payouts/payments")]
    public async Task<IActionResult> MarkPayoutPaid([FromBody] MarkPayoutPaidDto dto)
    {
        await _payouts.MarkPaidAsync(dto);
        return NoContent();
    }

    // כל הרחובות שיש בהם פריטים, והשכונה שנקבעה לכל רחוב
    [HttpGet("neighborhoods")]
    public async Task<IActionResult> GetNeighborhoods() => Ok(await _admin.GetNeighborhoodsAsync());

    public class StreetNeighborhoodDto
    {
        public string City { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string? Neighborhood { get; set; }
    }

    [HttpPut("neighborhoods")]
    public async Task<IActionResult> SetNeighborhood([FromBody] StreetNeighborhoodDto dto)
    {
        await _admin.SetNeighborhoodAsync(dto.City, dto.Street, dto.Neighborhood);
        return NoContent();
    }

    // כל הפריטים לשיוך מחדש לקטגוריה
    [HttpGet("items")]
    public async Task<IActionResult> GetItems() => Ok(await _admin.GetItemsAsync());

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
    public async Task<IActionResult> GetQuestions() => Ok(await _questions.GetAllForAdminAsync());

    public class EditMessageDto
    {
        public string Text { get; set; } = string.Empty;
    }

    // עריכת הודעה בהתכתבות (למשל פרטי קשר שנכתבו). טקסט ריק - מחיקת ההודעה
    [HttpPut("questions/{id}/messages/{index:int}")]
    public async Task<IActionResult> EditQuestionMessage([FromRoute] string id, [FromRoute] int index, [FromBody] EditMessageDto dto)
    {
        await _questions.EditMessageAsync(id, index, dto.Text);
        return NoContent();
    }

    [HttpGet("loans")]
    public async Task<IActionResult> GetLoans() => Ok(await _admin.GetLoansAsync());

    // מחיקת השכרה לצמיתות (למשל השכרת ניסיון)
    [HttpDelete("loans/{id}")]
    public async Task<IActionResult> DeleteLoan([FromRoute] string id)
    {
        await _admin.DeleteLoanAsync(id);
        return NoContent();
    }

    [HttpGet("favorites")]
    public async Task<IActionResult> GetFavorites() => Ok(await _admin.GetFavoritesAsync());

    [HttpGet("visits")]
    public async Task<IActionResult> GetVisits() => Ok(await _visits.GetAllAsync());

    // יומן שיחות הטלפון האוטומטיות (החדשות ראשונות)
    [HttpGet("calls")]
    public async Task<IActionResult> GetCalls() => Ok(await _calls.GetRecentAsync(CallLogLimit));

    // שליחה חוזרת של שיחת היכרות. תצא בסבב הבא של המתזמן (תוך כמה דקות)
    [HttpPost("calls/{id}/resends")]
    public async Task<IActionResult> ResendCall([FromRoute] string id) => Ok(await _calls.ResendWelcomeAsync(id));

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
        await _calls.CancelAsync(id);
        return NoContent();
    }
}
