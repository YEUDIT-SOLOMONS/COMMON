using PhotoRental.Core.DTOs;

namespace PhotoRental.Core.Interfaces;

// לוח יד 2. פעולות על רכישה מזוהות בקישור האישי (token) או במשתמשת המחוברת
public interface ISaleService
{
    Task<decimal> QuoteAsync(string itemId, DeliveryAddressDto address);

    Task<SaleCreatedDto> CreateAsync(string buyerId, SaleCreateDto dto);

    Task<IReadOnlyList<object>> MineAsync(string userId);

    // null = הרכישה לא נמצאה או שאין הרשאה לצפות בה
    Task<object?> GetViewAsync(string id, string? token, string? userId);

    Task<object> ConfirmReceivedAsync(string id, string? token, string? userId);

    Task<object> MarkHandedAsync(string id, string? token, string? userId, bool delivered);

    Task<object> ReportProblemAsync(string id, string? token, string? userId, string? text);

    Task<IReadOnlyList<object>> GetAdminListAsync();

    Task MarkSellerPaidAsync(string id);

    Task AdminConfirmReceivedAsync(string id);

    Task AutoConfirmAsync();
}
