namespace PhotoRental.Service.Services;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class FavoriteService : IFavoriteService
{
    private readonly IFavoriteRepository _favorites;
    private readonly IItemRepository _items;
    private readonly IItemService _itemService;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly ClientOptions _client;

    public FavoriteService(
        IFavoriteRepository favorites,
        IItemRepository items,
        IItemService itemService,
        IUserRepository users,
        IEmailService email,
        ClientOptions client)
    {
        _favorites = favorites;
        _items = items;
        _itemService = itemService;
        _users = users;
        _email = email;
        _client = client;
    }

    public async Task<IReadOnlyList<FavoriteDto>> GetMineAsync(string userId)
    {
        var result = new List<FavoriteDto>();
        foreach (var favorite in (await _favorites.GetByUserAsync(userId)).OrderByDescending(f => f.CreatedAtUtc))
        {
            if (!favorite.IsFavorite && !favorite.NotifyWhenAvailable)
                continue;
            result.Add(new FavoriteDto
            {
                ItemId = favorite.ItemId,
                IsFavorite = favorite.IsFavorite,
                NotifyWhenAvailable = favorite.NotifyWhenAvailable,
                Item = await _itemService.GetByIdAsync(favorite.ItemId)
            });
        }
        return result;
    }

    public async Task<FavoriteDto> UpdateAsync(string userId, string itemId, FavoriteUpdateDto dto)
    {
        var item = await _items.GetByIdAsync(itemId);
        if (item is null || item.DeletedAtUtc is not null)
            throw new KeyNotFoundException("הפריט לא נמצא");

        var favorite = await _favorites.GetAsync(userId, itemId)
            ?? new Favorite { UserId = userId, ItemId = itemId, CreatedAtUtc = DateTime.UtcNow };

        if (dto.IsFavorite is bool isFavorite)
            favorite.IsFavorite = isFavorite;
        if (dto.NotifyWhenAvailable is bool notify)
        {
            if (notify && item.Status == CopyStatus.Available)
                throw new ArgumentException("הפריט פנוי כבר עכשיו - אפשר להזמין אותו");
            favorite.NotifyWhenAvailable = notify;
            if (notify)
                favorite.NotifiedAtUtc = null;
        }

        if (favorite.IsFavorite || favorite.NotifyWhenAvailable)
            await _favorites.UpsertAsync(favorite);
        else
            await _favorites.DeleteAsync(userId, itemId);

        return new FavoriteDto { ItemId = itemId, IsFavorite = favorite.IsFavorite, NotifyWhenAvailable = favorite.NotifyWhenAvailable };
    }

    public async Task NotifyAvailableAsync()
    {
        var watchers = await _favorites.GetWatchersAsync();
        if (watchers.Count == 0)
            return;

        var baseUrl = _client.BaseUrl.TrimEnd('/');
        foreach (var group in watchers.GroupBy(w => w.ItemId))
        {
            var item = await _items.GetByIdAsync(group.Key);
            var gone = item is null || item.DeletedAtUtc is not null;
            if (!gone && item!.Status != CopyStatus.Available)
                continue;

            foreach (var watcher in group)
            {
                watcher.NotifyWhenAvailable = false;
                watcher.NotifiedAtUtc = DateTime.UtcNow;
                if (watcher.IsFavorite)
                    await _favorites.UpsertAsync(watcher);
                else
                    await _favorites.DeleteAsync(watcher.UserId, watcher.ItemId);

                if (gone || !_email.IsEnabled)
                    continue;

                var user = await _users.GetByIdAsync(watcher.UserId);
                if (user is null || string.IsNullOrWhiteSpace(user.Email))
                    continue;

                try
                {
                    string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
                    var link = $"{baseUrl}/?item={Uri.EscapeDataString(item!.Id ?? string.Empty)}";
                    var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                               $"<p>שלום {H(NameText.FirstName(user.Name))},</p>" +
                               $"<p><strong>{H(item.Name)}</strong> שביקשת לקבל עליו התראה התפנה עכשיו!</p>" +
                               "<p>כדאי להזמין מהר, לפני שמישהי אחרת תקדים אותך.</p>" +
                               $"<p><a href=\"{link}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>לפריט ולהזמנה</strong></a></p>" +
                               "<p>בהצלחה,<br/>common</p></div>";
                    await _email.SendAsync(user.Email, user.Name, $"{item.Name} התפנה - common", html);
                }
                catch
                {
                    // מייל שלא נשלח לא עוצר את שאר ההתראות
                }
            }
        }
    }
}
