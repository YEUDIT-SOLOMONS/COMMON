namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IPaymentDraftRepository
{
    Task<PaymentDraft> CreateAsync(PaymentDraft draft);
    Task<PaymentDraft?> GetByIdAsync(string id);
}
