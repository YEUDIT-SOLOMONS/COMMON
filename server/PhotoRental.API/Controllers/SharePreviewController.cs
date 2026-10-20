using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

namespace PhotoRental.API.Controllers;

// קישור שיתוף לפריט (commonrent.web.app/i/{id}, מנותב מ-Firebase Hosting לכאן): וואטסאפ קורא את תגי התצוגה
// המקדימה (תמונה, שם, מחיר), ומי שלוחצת עוברת מיד לחלון הפריט באתר
[ApiController]
[AllowAnonymous]
[Route("i")]
public class SharePreviewController : ControllerBase
{
    private readonly IItemRepository _items;
    private readonly ClientOptions _client;

    public SharePreviewController(IItemRepository items, ClientOptions client)
    {
        _items = items;
        _client = client;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Preview(string id)
    {
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var item = await _items.GetByIdAsync(id);
        if (item is null || item.DeletedAtUtc is not null)
            return Redirect(baseUrl + "/");

        string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var target = $"{baseUrl}/?item={Uri.EscapeDataString(id)}";
        var image = string.IsNullOrEmpty(item.ImageDataUrl) ? $"{baseUrl}/share.jpg" : $"{baseUrl}/i/{Uri.EscapeDataString(id)}/image";
        var title = $"{item.Name} - ₪{item.DailyPrice:0.##} ליום";
        var description = string.IsNullOrWhiteSpace(item.City)
            ? "להשכרה בקהילת הצלמות COMMON"
            : $"להשכרה בקהילת הצלמות COMMON · {item.City}";

        var html = "<!doctype html><html lang=\"he\" dir=\"rtl\"><head><meta charset=\"utf-8\">" +
                   $"<title>{H(title)} | COMMON</title>" +
                   "<meta property=\"og:type\" content=\"product\">" +
                   "<meta property=\"og:site_name\" content=\"COMMON\">" +
                   $"<meta property=\"og:title\" content=\"{H(title)}\">" +
                   $"<meta property=\"og:description\" content=\"{H(description)}\">" +
                   $"<meta property=\"og:image\" content=\"{H(image)}\">" +
                   $"<meta property=\"og:url\" content=\"{H($"{baseUrl}/i/{id}")}\">" +
                   "<meta property=\"og:locale\" content=\"he_IL\">" +
                   "<meta name=\"twitter:card\" content=\"summary_large_image\">" +
                   $"<meta http-equiv=\"refresh\" content=\"0;url={H(target)}\">" +
                   $"<script>location.replace({System.Text.Json.JsonSerializer.Serialize(target)})</script>" +
                   $"</head><body><a href=\"{H(target)}\">{H(item.Name)}</a></body></html>";

        Response.Headers.CacheControl = "public, max-age=300";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpGet("{id}/image")]
    public async Task<IActionResult> Image(string id)
    {
        var item = await _items.GetByIdAsync(id);
        var dataUrl = item?.ImageDataUrl;
        if (string.IsNullOrEmpty(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.Ordinal))
            return Redirect(_client.BaseUrl.TrimEnd('/') + "/share.jpg");

        var comma = dataUrl.IndexOf(',');
        var meta = dataUrl[5..comma];
        var mime = meta.Split(';')[0];
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return Redirect(_client.BaseUrl.TrimEnd('/') + "/share.jpg");
        }

        Response.Headers.CacheControl = "public, max-age=86400";
        return File(bytes, string.IsNullOrEmpty(mime) ? "image/jpeg" : mime);
    }
}
