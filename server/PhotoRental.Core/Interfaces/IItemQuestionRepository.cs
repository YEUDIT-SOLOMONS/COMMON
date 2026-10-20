namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IItemQuestionRepository
{
    Task<ItemQuestion?> GetByIdAsync(string id);
    Task<IReadOnlyList<ItemQuestion>> GetAllAsync();
    Task<ItemQuestion> CreateAsync(ItemQuestion question);
    Task UpdateAsync(ItemQuestion question);
    Task<string> AddImageAsync(string threadId, string dataUrl);
    Task<(string ThreadId, string DataUrl)?> GetImageAsync(string imageId);
}
