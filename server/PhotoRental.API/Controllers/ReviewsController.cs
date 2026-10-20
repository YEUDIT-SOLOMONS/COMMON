using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// דירוגים אחרי השכרה: דירוגי פריטים גלויים לכולם, דירוגי שוכרות - להנהלה בלבד
[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;

    public ReviewsController(IReviewService reviews)
    {
        _reviews = reviews;
    }

    [AllowAnonymous]
    [HttpGet("item/{itemId}")]
    public async Task<IActionResult> ForItem(string itemId) => Ok(await _reviews.GetItemReviewsAsync(itemId));

    [AllowAnonymous]
    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(await _reviews.GetSummaryAsync());

    [Authorize]
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _reviews.GetPendingAsync(userId));
    }

    [Authorize]
    [HttpPost("{loanId}")]
    public async Task<IActionResult> Submit(string loanId, [FromBody] ReviewSubmitDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await _reviews.SubmitAsync(userId, loanId, dto);
        return Ok(new { Message = "תודה על הדירוג!" });
    }

    [Authorize]
    [HttpGet("thanks")]
    public async Task<IActionResult> Thanks()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _reviews.GetThankYouNotesAsync(userId));
    }

    [Authorize]
    [HttpPost("{loanId}/dismiss")]
    public async Task<IActionResult> Dismiss(string loanId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        await _reviews.DismissAsync(userId, loanId);
        return Ok(new { Dismissed = true });
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> AdminList() => Ok(await _reviews.GetAllForAdminAsync());

    [Authorize(Roles = "Admin")]
    [HttpPut("admin/{reviewId}/hidden")]
    public async Task<IActionResult> SetHidden(string reviewId, [FromBody] ReviewHiddenDto dto)
    {
        await _reviews.SetHiddenAsync(reviewId, dto.Hidden);
        return Ok(new { dto.Hidden });
    }
}

public class ReviewHiddenDto
{
    public bool Hidden { get; set; }
}
