using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

[ApiController]
[Route("api/shipping-payments")]
[Authorize]
public class ShippingPayController : ControllerBase
{
    private readonly IShippingPaymentService _payments;

    public ShippingPayController(IShippingPaymentService payments)
    {
        _payments = payments;
    }

    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return Unauthorized();
        return Ok(await _payments.GetStatusAsync(CurrentUserId));
    }

    [HttpPost]
    public async Task<IActionResult> Start([FromBody] ShippingPayStartDto dto)
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return Unauthorized();
        return Ok(new { RedirectUrl = await _payments.BeginAsync(CurrentUserId, dto) });
    }

    [HttpPost("confirmation")]
    public async Task<IActionResult> Confirm([FromBody] ShippingPayConfirmDto dto)
    {
        if (string.IsNullOrEmpty(CurrentUserId)) return Unauthorized();
        return Ok(await _payments.ConfirmAsync(CurrentUserId, dto));
    }
}
