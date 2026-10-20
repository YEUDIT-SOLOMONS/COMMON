using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// עמלת השירות: כל משכירה רואה את הדוח שלה (האזור האישי), ומנהלות רואות ומנהלות את הגבייה של כולן
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CommissionsController : ControllerBase
{
    private readonly ICommissionService _commissionService;
    private readonly ICommissionDebtService _debtService;

    public CommissionsController(ICommissionService commissionService, ICommissionDebtService debtService)
    {
        _commissionService = commissionService;
        _debtService = debtService;
    }

    // מצב החוב של המשכירה המחוברת (ok / warning / blocked) - להודעה באזור האישי
    [HttpGet("debt")]
    public async Task<IActionResult> GetDebt()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _debtService.GetStatusAsync(userId));
    }

    // הדוח של המשכירה המחוברת לחודש (yyyy-MM), ברירת מחדל: החודש הנוכחי
    [HttpGet("my")]
    public async Task<IActionResult> GetMine([FromQuery] string? month)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _commissionService.GetOwnerReportAsync(userId, month));
    }

    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdminOverview([FromQuery] string? month)
    {
        return Ok(await _commissionService.GetAdminOverviewAsync(month));
    }

    [HttpPut("admin/paid")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> MarkPaid([FromBody] CommissionMarkPaidDto dto)
    {
        await _commissionService.SetPaidAsync(dto);
        return NoContent();
    }

    // תצוגה מקדימה של המייל שיישלח למשכירה (בלי לשלוח)
    [HttpGet("admin/preview")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> PreviewReport([FromQuery] string ownerId, [FromQuery] string month)
    {
        return Ok(await _commissionService.PreviewReportAsync(ownerId, month));
    }

    // אסמכתת ההעברה שהמשכירה העלתה דרך הקישור בדוח
    [HttpGet("admin/proof")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetProof([FromQuery] string ownerId, [FromQuery] string month)
    {
        var proof = await _commissionService.GetProofAsync(ownerId, month);
        return proof is null ? NotFound(new { Message = "לא הועלתה אסמכתא" }) : Ok(proof);
    }

    [HttpPost("admin/send")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SendReport([FromBody] CommissionSendDto dto)
    {
        var sent = await _commissionService.SendReportAsync(dto.OwnerId, dto.Month);
        return sent ? NoContent() : BadRequest(new { Message = "אין עמלה לחודש הזה - לא נשלח דוח" });
    }
}
