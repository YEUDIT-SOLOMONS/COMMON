namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IReviewRepository
{
    Task<IReadOnlyList<Review>> GetAllAsync();
    Task<IReadOnlyList<Review>> GetByItemAsync(string itemId);
    Task<IReadOnlyList<Review>> GetByOwnerAsync(string ownerId);
    Task<Review?> GetAsync(string loanId, string byRole);
    Task UpsertAsync(Review review);
    Task SetHiddenAsync(string id, bool hidden);
}
