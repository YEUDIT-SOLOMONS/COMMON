namespace PhotoRental.Service.Services;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// שכבת הלוגיקה העסקית לניהול קטגוריות ציוד
public class CategoryService : ICategoryService
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IItemRepository _itemRepository;

    public CategoryService(ICategoryRepository categoryRepository, IItemRepository itemRepository)
    {
        _categoryRepository = categoryRepository;
        _itemRepository = itemRepository;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync()
    {
        var categories = await _categoryRepository.GetAllAsync();
        // לפי הסדר שקבעה המנהלת, ובמקרה של שוויון (למשל קטגוריות ישנות בלי סדר) - לפי שם
        return categories
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Name, StringComparer.Create(new System.Globalization.CultureInfo("he-IL"), ignoreCase: true))
            .Select(MapToDto)
            .ToList();
    }

    public async Task<CategoryDto?> GetByIdAsync(string id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);
        return category is null ? null : MapToDto(category);
    }

    public async Task<CategoryDto> CreateAsync(CategoryCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם לקטגוריה");

        var name = dto.Name.Trim();
        var parentId = string.IsNullOrWhiteSpace(dto.ParentId) ? null : dto.ParentId;
        var existing = await _categoryRepository.GetAllAsync();

        if (parentId is not null)
        {
            var parent = existing.FirstOrDefault(c => c.Id == parentId)
                ?? throw new KeyNotFoundException("קטגוריית האב לא נמצאה");
            if (!string.IsNullOrEmpty(parent.ParentId))
                throw new ArgumentException("אי אפשר ליצור תת-קטגוריה בתוך תת-קטגוריה");
        }

        EnsureNameIsUnique(existing, name, parentId, exceptId: null);

        var siblings = existing.Where(c => c.ParentId == parentId).ToList();
        var category = new Category
        {
            Name = name,
            ParentId = parentId,
            Order = siblings.Count == 0 ? 0 : siblings.Max(c => c.Order) + 1
        };
        var created = await _categoryRepository.CreateAsync(category);
        return MapToDto(created);
    }

    public async Task<CategoryDto> UpdateAsync(string id, CategoryCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם לקטגוריה");

        var category = await _categoryRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("הקטגוריה לא נמצאה");

        var name = dto.Name.Trim();
        EnsureNameIsUnique(await _categoryRepository.GetAllAsync(), name, category.ParentId, exceptId: id);

        category.Name = name;
        await _categoryRepository.UpdateAsync(id, category);
        return MapToDto(category);
    }

    // שינוי סדר קבוצת אחים (קטגוריות ראשיות, או תתי-קטגוריות של אותה קטגוריה): הסדר נקבע לפי רשימת
    // המזהים שנשלחה; קטגוריות שלא ברשימה לא משתנות. הסדר מושווה רק בין אחים, ולכן אין התנגשות בין קבוצות.
    public async Task ReorderAsync(CategoryReorderDto dto)
    {
        var all = (await _categoryRepository.GetAllAsync()).ToDictionary(c => c.Id ?? string.Empty);
        var ids = dto.Ids.Where(all.ContainsKey).Distinct().ToList();

        for (var i = 0; i < ids.Count; i++)
        {
            var category = all[ids[i]];
            if (category.Order == i) continue;
            category.Order = i;
            await _categoryRepository.UpdateAsync(ids[i], category);
        }
    }

    public async Task DeleteAsync(string id)
    {
        var all = await _categoryRepository.GetAllAsync();
        var childCount = all.Count(c => c.ParentId == id);
        if (childCount > 0)
            throw new ArgumentException($"לא ניתן למחוק קטגוריה שיש בה {childCount} תתי-קטגוריות - יש למחוק אותן קודם");

        // לא מוחקים קטגוריה שעדיין יש בה פריטים - הם היו נשארים "יתומים" בלי קטגוריה
        var itemsInCategory = (await _itemRepository.GetByCategoryIdAsync(id)).Where(i => i.DeletedAtUtc is null).ToList();
        if (itemsInCategory.Count > 0)
            throw new ArgumentException($"לא ניתן למחוק קטגוריה שיש בה {itemsInCategory.Count} פריטים - יש להעביר או למחוק אותם קודם");

        await _categoryRepository.DeleteAsync(id);
    }

    // שם ייחודי בין אחים בלבד (שתי תתי-קטגוריות של קטגוריות שונות יכולות להיקרא "אחר")
    private static void EnsureNameIsUnique(IReadOnlyList<Category> all, string name, string? parentId, string? exceptId)
    {
        if (all.Any(c => c.Id != exceptId && c.ParentId == parentId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("כבר קיימת קטגוריה בשם הזה");
    }

    private static CategoryDto MapToDto(Category category) => new()
    {
        Id = category.Id ?? string.Empty,
        Name = category.Name,
        Order = category.Order,
        ParentId = category.ParentId
    };
}
