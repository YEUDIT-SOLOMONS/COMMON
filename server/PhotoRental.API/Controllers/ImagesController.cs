using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// תמונות הפריטים כקבצים רגילים: /img/{itemId}/thumb (התמונה הראשית) או /img/{itemId}/{n}.
// הקישור כולל טביעת תוכן (?v=), ולכן נשמר בקאש לשנה שלמה
[ApiController]
[Route("img")]
[AllowAnonymous]
public class ImagesController : ControllerBase
{
    private readonly IItemMediaService _media;

    public ImagesController(IItemMediaService media)
    {
        _media = media;
    }

    [HttpGet("{itemId}/{slot}")]
    public async Task<IActionResult> Get([FromRoute] string itemId, [FromRoute] string slot)
    {
        var image = await _media.GetImageAsync(itemId, slot);
        if (image is null)
            return NotFound();
        Response.Headers.CacheControl = "public, max-age=31536000, s-maxage=31536000, immutable";
        return File(image.Bytes, image.Mime);
    }
}
