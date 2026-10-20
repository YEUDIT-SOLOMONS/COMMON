namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IItemService
{
    Task<IReadOnlyList<ItemDto>> GetAllAsync();

    // "פנוי בתאריכים שלי": מזהי הפריטים שתפוסים (השכרה פעילה/עתידית או בתחזוקה) בטווח התאריכים, כולל
    Task<IReadOnlyList<string>> GetBusyItemIdsAsync(DateTime from, DateTime to);
    Task<ItemDto?> GetByIdAsync(string id);
    Task<IReadOnlyList<ItemDto>> GetByCategoryAsync(string categoryId);
    Task<IReadOnlyList<ItemDto>> GetByOwnerAsync(string ownerId);
    Task<ItemDto> CreateAsync(ItemCreateDto dto);
    Task<ItemDto> UpdateAsync(string id, ItemUpdateDto dto);

    // שינוי קטגוריה ע"י מנהלת (פריט שהוכנס לקטגוריה לא נכונה)
    Task SetCategoryAsync(string id, string categoryId, string? extraCategoryId);
    Task DeleteAsync(string id);

    // הכתובת המדויקת של הפריט - נחשפת רק בקריאה מפורשת ונפרדת זו (ראו ItemAddressDto)
    Task<ItemAddressDto> GetAddressAsync(string id);

    // פרטי חשבון הבנק של בעל הפריט, לשם ביצוע תשלום בהעברה בנקאית
    Task<UserPayoutInfoDto> GetOwnerPayoutAsync(string id);
}
