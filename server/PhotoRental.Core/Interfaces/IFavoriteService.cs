namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IFavoriteService
{
    Task<IReadOnlyList<FavoriteDto>> GetMineAsync(string userId);
    Task<FavoriteDto> UpdateAsync(string userId, string itemId, FavoriteUpdateDto dto);

    // לשירות הרקע: מייל למי שביקשה התראה על פריט שהתפנה
    Task NotifyAvailableAsync();
}
