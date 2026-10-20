using System.Net;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

namespace PhotoRental.Service.Services;

// תמונות הפריטים כקבצים, ותצוגה מקדימה לקישורי שיתוף
public class ItemMediaService : IItemMediaService
{
    private readonly IItemRepository _items;
    private readonly ClientOptions _client;

    public ItemMediaService(IItemRepository items, ClientOptions client)
    {
        _items = items;
        _client = client;
    }

    private string BaseUrl => _client.BaseUrl.TrimEnd('/');

    public string DefaultShareImageUrl => BaseUrl + "/share.jpg";

    public async Task<MediaFile?> GetImageAsync(string itemId, string slot)
    {
        var item = await _items.GetByIdAsync(itemId);
        if (item is null || item.DeletedAtUtc is not null || string.IsNullOrEmpty(item.ImageDataUrl))
            return null;
        var all = new[] { item.ImageDataUrl }.Concat(item.ExtraImageDataUrls).ToList();
        var dataUrl = slot == "thumb" ? all[0] : int.TryParse(slot, out var i) && i >= 0 && i < all.Count ? all[i] : null;
        return dataUrl is null ? null : Decode(dataUrl);
    }

    public async Task<SharePreview> GetSharePreviewAsync(string itemId)
    {
        var item = await _items.GetByIdAsync(itemId);
        if (item is null || item.DeletedAtUtc is not null)
            return new SharePreview(null, BaseUrl + "/");

        string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var target = $"{BaseUrl}/?item={Uri.EscapeDataString(itemId)}";
        var image = string.IsNullOrEmpty(item.ImageDataUrl) ? DefaultShareImageUrl : $"{BaseUrl}/i/{Uri.EscapeDataString(itemId)}/image";
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
                   $"<meta property=\"og:url\" content=\"{H($"{BaseUrl}/i/{itemId}")}\">" +
                   "<meta property=\"og:locale\" content=\"he_IL\">" +
                   "<meta name=\"twitter:card\" content=\"summary_large_image\">" +
                   $"<meta http-equiv=\"refresh\" content=\"0;url={H(target)}\">" +
                   $"<script>location.replace({System.Text.Json.JsonSerializer.Serialize(target)})</script>" +
                   $"</head><body><a href=\"{H(target)}\">{H(item.Name)}</a></body></html>";
        return new SharePreview(html, null);
    }

    public async Task<MediaFile?> GetShareImageAsync(string itemId)
    {
        var dataUrl = (await _items.GetByIdAsync(itemId))?.ImageDataUrl;
        return string.IsNullOrEmpty(dataUrl) ? null : Decode(dataUrl);
    }

    private static MediaFile? Decode(string dataUrl)
    {
        if (!dataUrl.StartsWith("data:", StringComparison.Ordinal))
            return null;
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
            return null;
        var type = dataUrl[5..comma].Split(';')[0];
        try
        {
            return new MediaFile(Convert.FromBase64String(dataUrl[(comma + 1)..]), string.IsNullOrEmpty(type) ? "image/jpeg" : type);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
