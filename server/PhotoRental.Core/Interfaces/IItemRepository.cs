namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;

public interface IItemRepository
{
    Task<IReadOnlyList<Item>> GetAllAsync();
    Task<Item?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, Item>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default);
    Task<(IReadOnlyList<Item> Items, int TotalCount)> GetPageAsync(ItemPageQueryDto query, CancellationToken ct = default);
    Task<IReadOnlyList<Item>> GetByCategoryIdAsync(string categoryId);
    Task<IReadOnlyList<Item>> GetByOwnerIdAsync(string ownerId);
    Task<Item> CreateAsync(Item item);
    Task UpdateAsync(string id, Item item);
    Task DeleteAsync(string id);

    // מחיקה רכה: מסמן את הפריט כמחוק (מוסתר מהקטלוג) בלי למחוק אותו - ההיסטוריה שלו נשמרת
    Task SoftDeleteAsync(string id, DateTime deletedAtUtc);

    // כותב רק את ארבעת שדות בדיקת "הפריט לא הושכר" (בלי לגעת בשאר הפריט)
    Task PatchStaleStateAsync(string id, Item item);

    // עדכון סטטוס הפריט בלבד (ובמידת הצורך מונה ההשכרות) - בלי לכתוב מחדש את כל הפריט עם התמונות
    Task UpdateStatusAsync(string id, CopyStatus status, int? rentalCount = null);

    Task PatchRentalCountAsync(string id, int rentalCount);
}
