namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;

// לוח זמנים לפריט: האם הטווח פנוי (השכרות פעילות/עתידיות וימים חסומים), חסימת ימים ע"י המשכירה,
// וסטטוס הפריט ("מושכר" רק כשהשכרה באמת בעיצומה - הזמנה עתידית לא חוסמת השכרות בתאריכים אחרים)
public interface IItemScheduleService
{
    Task<ItemScheduleDto> GetScheduleAsync(string itemId, string? userId);

    // זורק InvalidOperationException אם הטווח חופף השכרה אחרת או ימים חסומים
    Task EnsureFreeAsync(Item item, DateTime startLocal, DateTime endLocal, int units = 1);

    // מחשב מחדש את סטטוס הפריט לפי ההשכרות. newRentalCount - לעדכון מונה ההשכרות יחד עם הסטטוס
    Task RefreshStatusAsync(string itemId, int? newRentalCount = null);
    Task RefreshAllStatusesAsync();

    Task<ItemBlockDto> AddBlockAsync(string itemId, string userId, ItemBlockCreateDto dto);
    Task DeleteBlockAsync(string itemId, string blockId, string userId);

    Task<HashSet<string>> GetBlockedItemIdsAsync(DateTime from, DateTime to);
}
