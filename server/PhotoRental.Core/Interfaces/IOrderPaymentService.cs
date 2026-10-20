namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;

public interface IOrderPaymentService
{
    Task<ShippingPayStatusDto> GetStatusAsync(string userId);

    Task<string> BeginAsync(string userId, ShippingPayStartDto dto);

    Task<ShippingPayConfirmResultDto> ConfirmAsync(string userId, ShippingPayConfirmDto dto);

    // מאמת מול סאמיט שהתשלום תקף, בסכום המלא, של אותה משתמשת ועדיין לא שימש. זורק InvalidOperationException אם לא
    Task VerifyAsync(User payer, decimal expectedTotal, string? paymentId);
}
