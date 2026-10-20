namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IAvailabilityRequestRepository
{
    Task<AvailabilityRequest?> GetByIdAsync(string id);
    Task<AvailabilityRequest> CreateAsync(AvailabilityRequest request);
    Task UpdateAsync(AvailabilityRequest request);
    Task<IReadOnlyList<AvailabilityRequest>> GetByRenterAsync(string renterId);
    Task<IReadOnlyList<AvailabilityRequest>> GetByItemAsync(string itemId);
    Task<IReadOnlyList<AvailabilityRequest>> GetAnsweredAvailableAsync();
}
