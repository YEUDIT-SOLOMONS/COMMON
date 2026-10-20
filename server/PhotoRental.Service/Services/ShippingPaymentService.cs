namespace PhotoRental.Service.Services;

using System.Globalization;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class ShippingPaymentService : IShippingPaymentService
{
    private const string Description = "דמי משלוח - common";

    private readonly ICardPaymentService _cards;
    private readonly IUserRepository _users;
    private readonly ILoanRepository _loans;
    private readonly ClientOptions _client;
    private readonly ShippingChargeOptions _options;

    public ShippingPaymentService(
        ICardPaymentService cards, IUserRepository users, ILoanRepository loans, ClientOptions client, ShippingChargeOptions options, IPaymentDraftRepository drafts)
    {
        _drafts = drafts;
        _cards = cards;
        _users = users;
        _loans = loans;
        _client = client;
        _options = options;
    }

    private readonly IPaymentDraftRepository _drafts;

    private bool Enabled => _options.Enabled && _cards.IsEnabled;

    private static string IdentifierPrefix(string userId) => $"ship|{userId}|";

    public async Task<ShippingPayStatusDto> GetStatusAsync(string userId)
    {
        var user = await _users.GetByIdAsync(userId);
        return new ShippingPayStatusDto { Enabled = Enabled, HasCard = user?.ShippingCardSaved == true };
    }

    public async Task<string> BeginAsync(string userId, ShippingPayStartDto dto)
    {
        if (!Enabled)
            throw new InvalidOperationException("תשלום דמי משלוח באשראי אינו פעיל כרגע");
        if (!dto.Consent)
            throw new ArgumentException("יש לאשר שמירת הכרטיס לחיוב דמי משלוח בהזמנות עתידיות");
        if (dto.Amount <= 0)
            throw new ArgumentException("סכום דמי המשלוח אינו תקין");

        var user = await _users.GetByIdAsync(userId) ?? throw new KeyNotFoundException("המשתמשת לא נמצאה");
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var draftId = await OrderPaymentService.SaveDraftAsync(_drafts, userId, dto);

        return await _cards.BeginPaymentAsync(new CardPaymentRequest
        {
            Amount = dto.Amount,
            Description = Description,
            CustomerName = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            CustomerExternalId = user.Id!,
            ExternalIdentifier = IdentifierPrefix(userId) + draftId,
            RedirectUrl = $"{baseUrl}/shipping-paid",
            CancelUrl = $"{baseUrl}/",
            SavePaymentMethod = true
        });
    }

    public async Task<ShippingPayConfirmResultDto> ConfirmAsync(string userId, ShippingPayConfirmDto dto)
    {
        var notPaid = new ShippingPayConfirmResultDto();
        if (!Enabled || !long.TryParse(dto.PaymentId, NumberStyles.None, CultureInfo.InvariantCulture, out var paymentId))
            return notPaid;

        var info = await _cards.GetPaymentAsync(paymentId);
        if (info is null || !info.Valid || string.IsNullOrEmpty(info.ExternalIdentifier)
            || !info.ExternalIdentifier.StartsWith(IdentifierPrefix(userId), StringComparison.Ordinal))
            return notPaid;
        if (!string.IsNullOrEmpty(dto.ExternalIdentifier) && dto.ExternalIdentifier != info.ExternalIdentifier)
            return notPaid;

        await _users.UpdateShippingCardAsync(userId, DateTime.UtcNow);
        var result = new ShippingPayConfirmResultDto { Paid = true, PaymentId = paymentId.ToString(CultureInfo.InvariantCulture), Amount = info.Amount };
        await OrderPaymentService.AttachExistingLoanAsync(_loans, userId, result, shipping: true);
        if (result.ExistingLoanId is null)
            await OrderPaymentService.AttachDraftAsync(_drafts, userId, info.ExternalIdentifier, result);
        return result;
    }

    public async Task<ShippingChargeOutcome> ChargeAsync(User payer, decimal amount, string? paymentId)
    {
        if (amount <= 0)
            return new ShippingChargeOutcome("None", null);
        if (!Enabled)
            return new ShippingChargeOutcome("NotCharged", null);

        if (!string.IsNullOrWhiteSpace(paymentId))
            return await UseExistingPaymentAsync(payer, amount, paymentId);

        if (payer.ShippingCardSaved != true)
            throw new InvalidOperationException("יש להזין כרטיס אשראי לתשלום דמי המשלוח");

        var charge = await _cards.ChargeSavedCardAsync(new CardChargeRequest
        {
            Amount = amount,
            Description = Description,
            CustomerName = payer.Name,
            Email = payer.Email,
            Phone = payer.Phone,
            CustomerExternalId = payer.Id!
        });
        if (!charge.Success)
            throw new InvalidOperationException($"חיוב דמי המשלוח מהכרטיס השמור נכשל: {charge.Error}");

        return new ShippingChargeOutcome("Charged", charge.PaymentId?.ToString(CultureInfo.InvariantCulture));
    }

    private async Task<ShippingChargeOutcome> UseExistingPaymentAsync(User payer, decimal amount, string paymentId)
    {
        if (!long.TryParse(paymentId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new InvalidOperationException("אישור התשלום של דמי המשלוח אינו תקין");

        var info = await _cards.GetPaymentAsync(id);
        if (info is null || !info.Valid || string.IsNullOrEmpty(info.ExternalIdentifier)
            || !info.ExternalIdentifier.StartsWith(IdentifierPrefix(payer.Id!), StringComparison.Ordinal))
            throw new InvalidOperationException("לא הצלחנו לאמת את תשלום דמי המשלוח");
        if (info.Amount + 0.01m < amount)
            throw new InvalidOperationException("הסכום ששולם עבור המשלוח נמוך מהנדרש - נא לנסות שוב");

        var used = (await _loans.GetAllAsync()).Any(l => l.ShippingPaymentId == paymentId);
        if (used)
            throw new InvalidOperationException("התשלום הזה כבר שימש להזמנה אחרת");

        return new ShippingChargeOutcome("Charged", paymentId);
    }
}
