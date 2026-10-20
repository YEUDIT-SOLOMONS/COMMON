namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IItemBlockRepository
{
    Task<IReadOnlyList<ItemBlock>> GetAllAsync();
    Task<IReadOnlyList<ItemBlock>> GetByItemAsync(string itemId);
    Task<ItemBlock?> GetByIdAsync(string id);
    Task<ItemBlock> CreateAsync(ItemBlock block);
    Task DeleteAsync(string id);
}
