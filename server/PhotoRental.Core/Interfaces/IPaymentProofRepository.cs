namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface IPaymentProofRepository
{
    Task<PaymentProof> CreateAsync(PaymentProof proof);
    Task<PaymentProof?> GetByIdAsync(string id);
}
