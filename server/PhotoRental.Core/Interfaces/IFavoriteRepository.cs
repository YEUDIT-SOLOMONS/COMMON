namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IFavoriteRepository
{
    Task<Favorite?> GetAsync(string userId, string itemId);
    Task<IReadOnlyList<Favorite>> GetByUserAsync(string userId);
    Task<IReadOnlyList<Favorite>> GetWatchersAsync();
    Task<IReadOnlyList<Favorite>> GetAllAsync();
    Task UpsertAsync(Favorite favorite);
    Task DeleteAsync(string userId, string itemId);
}
