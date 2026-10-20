using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// מועדפים ו"תודיעו לי כשמתפנה" של המשתמשת המחוברת
[ApiController]
[Route("api/favorites")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly IFavoriteService _favorites;

    public FavoritesController(IFavoriteService favorites)
    {
        _favorites = favorites;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _favorites.GetMineAsync(userId));
    }

    [HttpPut("{itemId}")]
    public async Task<IActionResult> Update(string itemId, [FromBody] FavoriteUpdateDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        return Ok(await _favorites.UpdateAsync(userId, itemId, dto));
    }
}
