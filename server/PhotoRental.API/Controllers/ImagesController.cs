using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace PhotoRental.API.Controllers;

// תמונות הפריטים כקבצים רגילים: /img/{itemId}/thumb (תמונה קטנה לכרטיסים) או /img/{itemId}/{n} (בגודל מלא).
// הקישור כולל טביעת תוכן (?v=), ולכן נשמר בקאש לשנה שלמה
[ApiController]
[Route("img")]
[AllowAnonymous]
public class ImagesController : ControllerBase
{
    private const int ThumbSize = 420;
    private static readonly ConcurrentDictionary<string, byte[]> Thumbs = new();
    private readonly IItemRepository _items;

    public ImagesController(IItemRepository items)
    {
        _items = items;
    }

    [HttpGet("{itemId}/{slot}")]
    public async Task<IActionResult> Get(string itemId, string slot)
    {
        var item = await _items.GetByIdAsync(itemId);
        if (item is null || item.DeletedAtUtc is not null || string.IsNullOrEmpty(item.ImageDataUrl))
            return NotFound();

        var all = new[] { item.ImageDataUrl }.Concat(item.ExtraImageDataUrls).ToList();
        string? dataUrl = slot == "thumb" ? all[0] : int.TryParse(slot, out var i) && i >= 0 && i < all.Count ? all[i] : null;
        if (dataUrl is null || !TryDecode(dataUrl, out var bytes, out var mime))
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, s-maxage=31536000, immutable";

        if (slot != "thumb")
            return File(bytes, mime);

        var key = $"{itemId}:{PhotoRental.Core.Helpers.ImageLinks.Version(dataUrl)}";
        if (!Thumbs.TryGetValue(key, out var thumb))
        {
            try
            {
                using var image = Image.Load(bytes);
                if (image.Width > ThumbSize || image.Height > ThumbSize)
                    image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(ThumbSize, ThumbSize), Mode = ResizeMode.Max }));
                using var output = new MemoryStream();
                image.SaveAsJpeg(output, new JpegEncoder { Quality = 72 });
                thumb = output.ToArray();
            }
            catch
            {
                return File(bytes, mime);
            }
            if (Thumbs.Count > 500) Thumbs.Clear();
            Thumbs[key] = thumb;
        }
        return File(thumb, "image/jpeg");
    }

    private static bool TryDecode(string dataUrl, out byte[] bytes, out string mime)
    {
        bytes = Array.Empty<byte>();
        mime = "image/jpeg";
        if (!dataUrl.StartsWith("data:", StringComparison.Ordinal))
            return false;
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
            return false;
        var type = dataUrl[5..comma].Split(';')[0];
        if (!string.IsNullOrEmpty(type)) mime = type;
        try
        {
            bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
