namespace PhotoRental.Service.Services;

using System.Globalization;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class ExtraChargeService : IExtraChargeService
{
    private readonly ICardPaymentService _cards;
    private readonly ShippingChargeOptions _options;

    public ExtraChargeService(ICardPaymentService cards, ShippingChargeOptions options)
    {
        _cards = cards;
        _options = options;
    }

    public async Task<ExtraChargeResult> ChargeAsync(User payer, decimal amount, string description)
    {
        if (amount <= 0)
            return new ExtraChargeResult { Success = true };
        if (!_options.Enabled || !_cards.IsEnabled)
            return new ExtraChargeResult { Error = "החיוב האוטומטי מהכרטיס אינו פעיל כרגע" };
        if (!payer.ShippingCardSaved)
            return new ExtraChargeResult { Error = "לא נשמר כרטיס אשראי לחיוב" };

        var charge = await _cards.ChargeSavedCardAsync(new CardChargeRequest
        {
            Amount = amount,
            Description = description,
            CustomerName = payer.Name,
            Email = payer.Email,
            Phone = payer.Phone,
            CustomerExternalId = payer.Id!
        });
        return charge.Success
            ? new ExtraChargeResult { Success = true, PaymentId = charge.PaymentId?.ToString(CultureInfo.InvariantCulture) }
            : new ExtraChargeResult { Error = charge.Error };
    }
}
