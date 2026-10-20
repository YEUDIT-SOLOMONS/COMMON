namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync();
    Task<CategoryDto?> GetByIdAsync(string id);
    Task<CategoryDto> CreateAsync(CategoryCreateDto dto);
    Task<CategoryDto> UpdateAsync(string id, CategoryCreateDto dto);
    Task ReorderAsync(CategoryReorderDto dto);
    Task DeleteAsync(string id);
}
