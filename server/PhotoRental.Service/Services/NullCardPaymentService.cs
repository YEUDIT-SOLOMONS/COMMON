namespace PhotoRental.Service.Services;

using PhotoRental.Core.Interfaces;

// כשלא הוגדרו מפתחות סאמיט: אין תשלום באשראי (הדוח החודשי כולל רק פרטי העברה בנקאית)
public class NullCardPaymentService : ICardPaymentService
{
    public bool IsEnabled => false;

    public Task<string> BeginPaymentAsync(CardPaymentRequest request) =>
        throw new InvalidOperationException("התשלום באשראי אינו מוגדר בשרת");

    public Task<CardChargeResult> ChargeSavedCardAsync(CardChargeRequest request) =>
        Task.FromResult(new CardChargeResult { Error = "התשלום באשראי אינו מוגדר בשרת" });

    public Task<CardPaymentInfo?> GetPaymentAsync(long paymentId) => Task.FromResult<CardPaymentInfo?>(null);
}
