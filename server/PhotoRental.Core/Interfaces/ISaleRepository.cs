namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface ISaleRepository
{
    Task<Sale?> GetByIdAsync(string id);
    Task<IReadOnlyList<Sale>> GetAllAsync();
    Task<Sale> CreateAsync(Sale sale);
    Task UpdateAsync(Sale sale);
}
