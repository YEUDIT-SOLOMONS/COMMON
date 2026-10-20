namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;

public record ShippingChargeOutcome(string Status, string? PaymentId);

public interface IShippingPaymentService
{
    Task<ShippingPayStatusDto> GetStatusAsync(string userId);

    Task<string> BeginAsync(string userId, ShippingPayStartDto dto);

    Task<ShippingPayConfirmResultDto> ConfirmAsync(string userId, ShippingPayConfirmDto dto);

    // Status: None / NotCharged (חיוב כבוי) / Charged. זורק InvalidOperationException אם אי אפשר לגבות
    Task<ShippingChargeOutcome> ChargeAsync(User payer, decimal amount, string? paymentId);
}
