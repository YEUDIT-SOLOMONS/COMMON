using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// קישור שיתוף לפריט (commonrent.web.app/i/{id}): וואטסאפ קורא את תגי התצוגה המקדימה (תמונה, שם, מחיר),
// ומי שלוחצת עוברת מיד לחלון הפריט באתר
[ApiController]
[AllowAnonymous]
[Route("i")]
public class SharePreviewController : ControllerBase
{
    private readonly IItemMediaService _media;

    public SharePreviewController(IItemMediaService media)
    {
        _media = media;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Preview([FromRoute] string id)
    {
        var preview = await _media.GetSharePreviewAsync(id);
        if (preview.Html is null)
            return Redirect(preview.RedirectUrl!);
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(preview.Html, "text/html; charset=utf-8");
    }

    [HttpGet("{id}/image")]
    public async Task<IActionResult> Image([FromRoute] string id)
    {
        var image = await _media.GetShareImageAsync(id);
        if (image is null)
            return Redirect(_media.DefaultShareImageUrl);
        Response.Headers.CacheControl = "public, max-age=86400";
        return File(image.Bytes, image.Mime);
    }
}
