namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;

public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetAllAsync();
    Task<User?> GetByIdAsync(string id);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByPasswordResetTokenAsync(string token);
    Task<User> CreateAsync(User user);
    Task UpdateAddressAsync(string id, string address, string city, string? addressNotes, string? floor, string? apartment, string? street = null);
    Task SetPasswordResetTokenAsync(string id, string? token, DateTime? expiresUtc);
    Task UpdatePasswordAsync(string id, string passwordHash);
    Task UpdateProfileAsync(string id, string name, string phone);
    Task UpdateQuietHoursAsync(string id, bool enabled, string? from, string? to);
    Task UpdateLastLoginAsync(string id, DateTime loginAtUtc);
    Task UpdateCommissionConsentAsync(string id, DateTime consentAtUtc);
    Task UpdateSaleCommissionConsentAsync(string id, DateTime consentAtUtc);
    Task UpdateAutoChargeAsync(string id, DateTime? consentAtUtc, bool enabled);
    Task UpdateShippingCardAsync(string id, DateTime consentAtUtc);
    Task UpdatePayoutAsync(
        string id,
        string accountHolderName,
        string bankName,
        string branchNumber,
        string accountNumber,
        DateTime consentAtUtc);

    // מצפין מספרי חשבון שנשמרו כטקסט גלוי לפני שההצפנה הופעלה. מחזיר כמה עודכנו.
    Task<int> EncryptLegacyPayoutAccountNumbersAsync();
    Task DeleteAsync(string id);
}
