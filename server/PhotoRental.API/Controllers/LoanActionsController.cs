using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// "חלון הסימון" של המשכירה: הקישורים שנשלחים אליה במייל (אישור השכרה, ו"הפריט נאסף?" 5 דקות אחרי תחילת
// ההשכרה) נפתחים בלי התחברות, ומוגנים בטוקן סודי ארוך שייחודי לכל השכרה (?t=...). טוקן שגוי = 404.
[ApiController]
[Route("api/loan-actions")]
[AllowAnonymous]
public class LoanActionsController : ControllerBase
{
    private readonly ILoanService _loanService;
    private readonly ILoanExtensionService _extensions;

    public LoanActionsController(ILoanService loanService, ILoanExtensionService extensions)
    {
        _loanService = loanService;
        _extensions = extensions;
    }

    // אישור (a=1) או דחייה (a=2) של בקשת הארכה - מהמייל שנשלח כשהמשכירה לא ענתה לשיחה
    [HttpPost("{loanId}/extension")]
    public async Task<IActionResult> AnswerExtension(string loanId, [FromQuery(Name = "t")] string? token, [FromQuery(Name = "a")] int answer)
    {
        if (!await AuthorizedAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        if (answer is not (1 or 2)) return BadRequest(new { Message = "תשובה לא תקינה" });
        var message = await _extensions.OwnerAnsweredAsync(loanId, answer.ToString());
        return Ok(new { Message = message.Replace(" להתראות.", string.Empty).Replace(". להתראות", ".") });
    }

    private async Task<bool> AuthorizedAsync(string loanId, string? token) =>
        await _loanService.ValidateOwnerTokenAsync(loanId, token);

    [HttpGet("{loanId}")]
    public async Task<IActionResult> Get(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await AuthorizedAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        var view = await _loanService.GetActionViewAsync(loanId);
        return view is null ? NotFound(new { Message = "ההשכרה לא נמצאה" }) : Ok(view);
    }

    [HttpPost("{loanId}/picked-up")]
    public async Task<IActionResult> MarkPickedUp(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await AuthorizedAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        await _loanService.MarkPickedUpAsync(loanId, "Owner");
        return NoContent();
    }

    [HttpPost("{loanId}/returned")]
    public async Task<IActionResult> MarkReturned(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await AuthorizedAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        await _loanService.MarkReturnedFromLinkAsync(loanId);
        return NoContent();
    }

    [HttpPost("{loanId}/not-returned")]
    public async Task<IActionResult> MarkNotReturned(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await AuthorizedAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        return Ok(await _loanService.ReportNotReturnedAsync(loanId));
    }

    [HttpPost("{loanId}/payment-received")]
    public async Task<IActionResult> MarkPaymentReceived(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await AuthorizedAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        await _loanService.ConfirmPaymentAsync(loanId);
        return NoContent();
    }

    [HttpGet("{loanId}/payment-proof")]
    public async Task<IActionResult> GetPaymentProof(string loanId, [FromQuery(Name = "t")] string? token)
    {
        if (!await AuthorizedAsync(loanId, token)) return NotFound(new { Message = "הקישור אינו תקף" });
        var proof = await _loanService.GetPaymentProofAsync(loanId);
        return proof is null ? NotFound(new { Message = "לא הועלתה אסמכתא להשכרה זו" }) : Ok(proof);
    }
}
