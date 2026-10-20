using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// עסקאות השכרה. כל הנקודות דורשות התחברות, וכל משתמשת רואה ופועלת רק על ההשכרות שלה (כשוכרת או כמשכירה);
// רק מנהלות רואות את כולן. הרשימה הכללית נחשפת רק כמספרים (popularity) לצורך מיון הקטלוג.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LoansController : ControllerBase
{
    private readonly ILoanService _loanService;

    public LoansController(ILoanService loanService)
    {
        _loanService = loanService;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsAdmin => User.IsInRole("Admin");
    private bool IsSelfOrAdmin(string? id) => IsAdmin || (!string.IsNullOrEmpty(id) && CurrentUserId == id);

    // שליפת כל עסקאות ההשכרה - למנהלות בלבד
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        var loans = await _loanService.GetAllAsync();
        return Ok(loans);
    }

    // כמה פעמים כל פריט הושכר (itemId -> מספר) - פתוח לכולן, מספרים בלבד, למיון "המבוקשים בקהילה"
    [HttpGet("popularity")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPopularity()
    {
        return Ok(await _loanService.GetRentalCountsAsync());
    }

    // ההשכרות שלי כשוכרת (האזור האישי - "ההשכרות שלי")
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUser([FromRoute] string userId, [FromQuery] int? limit)
    {
        if (!IsSelfOrAdmin(userId)) return Forbid();
        var loans = await _loanService.GetByBorrowerIdAsync(userId, limit);
        return Ok(loans);
    }

    // השכרות על הציוד שלי כמשכירה (האזור האישי - "השכרות על הציוד שלי")
    [HttpGet("incoming/{ownerId}")]
    public async Task<IActionResult> GetIncoming([FromRoute] string ownerId, [FromQuery] int? limit)
    {
        if (!IsSelfOrAdmin(ownerId)) return Forbid();
        return Ok(await _loanService.GetByOwnerIdAsync(ownerId, limit));
    }

    // הצעת מחיר משלוח אמיתית (רגיל/אקספרס) - נקראת מהלקוח מיד כשנבחר "משלוח" והוזנה כתובת, עוד
    // לפני אימות הזמינות, כדי שהמחיר המוצג יהיה תמיד המחיר האמיתי (ראו ILoanService.GetShippingQuoteAsync)
    [HttpPost("shipping-quotes")]
    public async Task<IActionResult> GetShippingQuote([FromBody] ShippingQuoteRequestDto request)
    {
        var quote = await _loanService.GetShippingQuoteAsync(request);
        return Ok(quote);
    }

    // פתיחת עסקת השכרה חדשה - רק על שם המשתמשת המחוברת (או מנהלת)
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] LoanRequestDto request, CancellationToken ct)
    {
        if (!IsSelfOrAdmin(request.BorrowerId)) return Forbid();
        var loan = await _loanService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetOrder), new { id = loan.Id }, loan);
    }

    // פתיחת הזמנה אחת הכוללת כמה פריטים מאותו חבר קהילה (משכיר) בבת אחת (ראו LoanBatchRequestDto)
    [HttpPost("batch")]
    public async Task<IActionResult> CreateBatch([FromBody] LoanBatchRequestDto request, CancellationToken ct)
    {
        if (!IsSelfOrAdmin(request.BorrowerId)) return Forbid();
        var loans = await _loanService.CreateBatchAsync(request, ct);
        return CreatedAtAction(nameof(GetOrder), new { id = loans[0].Id }, loans);
    }

    // עדכון סטטוס השכרה ידני (Active / Returned / Overdue / Cancelled) - למנהלות בלבד
    [HttpPut("{id}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateStatus([FromRoute] string id, [FromBody] string newStatus)
    {
        await _loanService.UpdateStatusAsync(id, newStatus);
        return NoContent();
    }

    // סימון "הפריט נאסף" מהאתר - המשכירה, השוכרת או מנהלת. אותו שדה כמו בחלון הסימון שבמייל, ולכן הכול מסונכרן
    [HttpPost("{id}/pickup")]
    public async Task<IActionResult> MarkPickedUp([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.OwnerId && CurrentUserId != participants.BorrowerId) return Forbid();

        await _loanService.MarkPickedUpAsync(id, CurrentUserId == participants.BorrowerId ? "Renter" : "Owner");
        return NoContent();
    }

    // ביטול השכרה: השוכרת, המשכירה או מנהלת. הכללים (24 שעות לפני תחילה) נאכפים בשירות
    [HttpPost("{id}/cancellation")]
    public async Task<IActionResult> Cancel([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });

        var actor = IsAdmin ? "Admin" : CurrentUserId == participants.OwnerId ? "Owner" : CurrentUserId == participants.BorrowerId ? "Renter" : null;
        if (actor is null) return Forbid();

        return Ok(await _loanService.CancelLoanAsync(id, actor, IsAdmin));
    }

    // שינוי אופן ההחזרה באמצע ההשכרה: השוכרת (או מנהלת) בלבד
    [HttpPut("{id}/return-method")]
    public async Task<IActionResult> ChangeReturnMethod([FromRoute] string id, [FromBody] ChangeReturnMethodDto request)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.BorrowerId) return Forbid();

        return Ok(await _loanService.ChangeReturnMethodAsync(id, request));
    }

    // מסך "ההזמנה בוצעה" - פרטי ההזמנה (כל הפריטים) לשוכרת, למשכירה ולמנהלות בלבד
    [HttpGet("{id}/order")]
    public async Task<IActionResult> GetOrder([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההזמנה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.BorrowerId && CurrentUserId != participants.OwnerId) return Forbid();

        return Ok(await _loanService.GetOrderAsync(id));
    }

    // מצב משלוח חי (זמן הגעה, שליח, מיקום) - לשוכרת, למשכירה ולמנהלות בלבד
    [HttpGet("{id}/shipping-status")]
    public async Task<IActionResult> GetShippingStatus([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.BorrowerId && CurrentUserId != participants.OwnerId) return Forbid();

        return Ok(await _loanService.GetShippingStatusAsync(id));
    }

    // סימון השכרה כמוחזרת - המשכירה (בעלת הפריט) או מנהלת בלבד; השוכרת אינה יכולה לסמן החזרה בעצמה, כדי למנוע הונאה
    [HttpPost("{id}/return-confirmation")]
    public async Task<IActionResult> Return([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.OwnerId) return Forbid();

        await _loanService.ReturnLoanAsync(id);
        return NoContent();
    }

    // אישור קבלת תשלום ידני (מזומן/העברה בנקאית) - למשכירה (בעלת הפריט) בלבד, לאחר שקיבלה את התשלום.
    // פותח את חשיפת פרטי הקשר שלה לשוכרת כאשר אופן הקבלה הוא איסוף עצמי.
    [HttpPost("{id}/payment-confirmation")]
    public async Task<IActionResult> ConfirmPayment([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.OwnerId) return Forbid();

        await _loanService.ConfirmPaymentAsync(id);
        return NoContent();
    }

    // סיכום החודש למשכירה (פתק באזור האישי)
    [HttpGet("owner-summary/{ownerId}")]
    public async Task<IActionResult> OwnerSummary([FromRoute] string ownerId)
    {
        if (!IsSelfOrAdmin(ownerId)) return Forbid();
        return Ok(await _loanService.GetOwnerMonthSummaryAsync(ownerId));
    }

    // אסמכתת ההעברה של האתר למשכירה (השכרה ששולמה באשראי) - למשכירה ולמנהלות בלבד
    [HttpGet("{id}/payout-proof")]
    public async Task<IActionResult> GetPayoutProof([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.OwnerId) return Forbid();

        var proof = await _loanService.GetPayoutProofAsync(id);
        return proof is null ? NotFound(new { Message = "לא הועלתה אסמכתא להעברה זו" }) : Ok(proof);
    }

    // אסמכתת ההעברה הבנקאית של השכרה - לשוכרת שהעלתה אותה, למשכירה ולמנהלות בלבד
    [HttpGet("{id}/payment-proof")]
    public async Task<IActionResult> GetPaymentProof([FromRoute] string id)
    {
        var participants = await _loanService.GetParticipantsAsync(id);
        if (participants is null) return NotFound(new { Message = "ההשכרה לא נמצאה" });
        if (!IsAdmin && CurrentUserId != participants.BorrowerId && CurrentUserId != participants.OwnerId) return Forbid();

        var proof = await _loanService.GetPaymentProofAsync(id);
        return proof is null ? NotFound(new { Message = "לא הועלתה אסמכתא להשכרה זו" }) : Ok(proof);
    }
}
