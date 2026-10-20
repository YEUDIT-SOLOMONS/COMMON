using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly ISaleService _sales;

    public SalesController(ISaleService sales)
    {
        _sales = sales;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    public class QuoteDto
    {
        public string ItemId { get; set; } = string.Empty;
        public DeliveryAddressDto DeliveryAddress { get; set; } = new();
    }

    public class ProblemDto
    {
        public string Text { get; set; } = string.Empty;
    }

    [Authorize]
    [HttpPost("quotes")]
    public async Task<IActionResult> Quote([FromBody] QuoteDto dto) =>
        Ok(new { Fee = await _sales.QuoteAsync(dto.ItemId, dto.DeliveryAddress) });

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaleCreateDto dto)
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        var created = await _sales.CreateAsync(UserId, dto);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        if (string.IsNullOrEmpty(UserId)) return Unauthorized();
        return Ok(await _sales.MineAsync(UserId));
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get([FromRoute] string id, [FromQuery] string? t)
    {
        var view = await _sales.GetViewAsync(id, t, UserId);
        return view is null ? NotFound(new { Message = "הרכישה לא נמצאה" }) : Ok(view);
    }

    [AllowAnonymous]
    [HttpPost("{id}/receipt")]
    public async Task<IActionResult> Received([FromRoute] string id, [FromQuery] string? t) =>
        Ok(await _sales.ConfirmReceivedAsync(id, t, UserId));

    [AllowAnonymous]
    [HttpPost("{id}/handover")]
    public async Task<IActionResult> Handed([FromRoute] string id, [FromQuery] string? t, [FromQuery] bool delivered = true) =>
        Ok(await _sales.MarkHandedAsync(id, t, UserId, delivered));

    [AllowAnonymous]
    [HttpPost("{id}/problems")]
    public async Task<IActionResult> Problem([FromRoute] string id, [FromQuery] string? t, [FromBody] ProblemDto dto) =>
        Ok(await _sales.ReportProblemAsync(id, t, UserId, dto.Text));

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> AdminList() => Ok(await _sales.GetAdminListAsync());

    [Authorize(Roles = "Admin")]
    [HttpPost("admin/{id}/seller-payment")]
    public async Task<IActionResult> SellerPaid([FromRoute] string id)
    {
        await _sales.MarkSellerPaidAsync(id);
        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("admin/{id}/receipt")]
    public async Task<IActionResult> AdminReceived([FromRoute] string id)
    {
        await _sales.AdminConfirmReceivedAsync(id);
        return NoContent();
    }
}
