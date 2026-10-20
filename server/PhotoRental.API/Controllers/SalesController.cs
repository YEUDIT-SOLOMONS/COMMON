using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;
using PhotoRental.Service.Services;

namespace PhotoRental.API.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly SaleService _sales;
    private readonly IUserRepository _users;

    public SalesController(SaleService sales, IUserRepository users)
    {
        _sales = sales;
        _users = users;
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
        var sale = await _sales.CreateAsync(UserId, dto);
        return CreatedAtAction(nameof(Get), new { id = sale.Id }, new { sale.Id, Token = sale.BuyerToken });
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
        var found = await _sales.ResolveAsync(id, t, UserId);
        if (found is null) return NotFound(new { Message = "הרכישה לא נמצאה" });
        return Ok(await _sales.ViewAsync(found.Value.Sale, found.Value.Role));
    }

    [AllowAnonymous]
    [HttpPost("{id}/receipt")]
    public async Task<IActionResult> Received([FromRoute] string id, [FromQuery] string? t)
    {
        var found = await _sales.ResolveAsync(id, t, UserId);
        if (found is null || found.Value.Role != "Buyer") return NotFound(new { Message = "הרכישה לא נמצאה" });
        await _sales.ConfirmReceivedAsync(found.Value.Sale);
        return Ok(await _sales.ViewAsync(found.Value.Sale, "Buyer"));
    }

    [AllowAnonymous]
    [HttpPost("{id}/handover")]
    public async Task<IActionResult> Handed([FromRoute] string id, [FromQuery] string? t, [FromQuery] bool delivered = true)
    {
        var found = await _sales.ResolveAsync(id, t, UserId);
        if (found is null || found.Value.Role != "Seller") return NotFound(new { Message = "המכירה לא נמצאה" });
        await _sales.MarkHandedAsync(found.Value.Sale, delivered);
        return Ok(await _sales.ViewAsync(found.Value.Sale, "Seller"));
    }

    [AllowAnonymous]
    [HttpPost("{id}/problems")]
    public async Task<IActionResult> Problem([FromRoute] string id, [FromQuery] string? t, [FromBody] ProblemDto dto)
    {
        var found = await _sales.ResolveAsync(id, t, UserId);
        if (found is null || found.Value.Role != "Buyer") return NotFound(new { Message = "הרכישה לא נמצאה" });
        if (string.IsNullOrWhiteSpace(dto.Text)) return BadRequest(new { Message = "יש לתאר את הבעיה" });
        await _sales.ReportProblemAsync(found.Value.Sale, dto.Text);
        return Ok(await _sales.ViewAsync(found.Value.Sale, "Buyer"));
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> AdminList()
    {
        var rows = new List<object>();
        foreach (var s in (await _sales.GetAllAsync()).OrderByDescending(s => s.CreatedAtUtc))
        {
            var seller = await _users.GetByIdAsync(s.SellerId);
            var buyer = await _users.GetByIdAsync(s.BuyerId);
            rows.Add(new
            {
                s.Id, s.ItemId, s.ItemName, s.PaymentMethod, s.Price, s.DeliveryFee, s.Commission, s.SellerAmount, s.DeliveryMethod, s.DeliveryAddress,
                s.TrackingId, s.DispatchError, s.Status, s.HandedOverAtUtc, s.NotHandedAtUtc, s.ProblemText, s.CreatedAtUtc, s.ReceivedAtUtc, s.AutoConfirmed, s.SellerPaidAtUtc,
                s.PaymentTransactionId,
                SellerName = seller?.Name, SellerPhone = seller?.Phone, SellerEmail = seller?.Email,
                BuyerName = buyer?.Name, BuyerPhone = buyer?.Phone, BuyerEmail = buyer?.Email
            });
        }
        return Ok(rows);
    }

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
        var found = (await _sales.GetAllAsync()).FirstOrDefault(s => s.Id == id);
        if (found is null) return NotFound();
        found.Status = "Paid";
        await _sales.ConfirmReceivedAsync(found);
        return NoContent();
    }
}
