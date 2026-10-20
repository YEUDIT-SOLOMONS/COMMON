namespace PhotoRental.Service.Services;

using System.Globalization;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class OrderPaymentService : IOrderPaymentService
{
    private const string Description = "השכרת ציוד - common";

    private readonly ICardPaymentService _cards;
    private readonly IUserRepository _users;
    private readonly ILoanRepository _loans;
    private readonly ClientOptions _client;
    private readonly IPaymentDraftRepository _drafts;

    public OrderPaymentService(ICardPaymentService cards, IUserRepository users, ILoanRepository loans, ClientOptions client, IPaymentDraftRepository drafts)
    {
        _cards = cards;
        _users = users;
        _loans = loans;
        _client = client;
        _drafts = drafts;
    }

    // מזהה ההזמנה השמורה הוא החלק האחרון במזהה התשלום
    internal static async Task<string> SaveDraftAsync(IPaymentDraftRepository drafts, string userId, ShippingPayStartDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Draft) || dto.Draft.Length > 900_000)
            return Guid.NewGuid().ToString("N");
        var saved = await drafts.CreateAsync(new PaymentDraft { UserId = userId, Draft = dto.Draft, IsBatch = dto.IsBatch, CreatedAtUtc = DateTime.UtcNow });
        return saved.Id ?? Guid.NewGuid().ToString("N");
    }

    internal static async Task AttachExistingLoanAsync(ILoanRepository loans, string userId, ShippingPayConfirmResultDto result, bool shipping)
    {
        var key = result.PaymentId;
        if (string.IsNullOrEmpty(key))
            return;
        var existing = (await loans.GetAllAsync()).FirstOrDefault(l =>
            l.BorrowerId == userId && (shipping ? l.ShippingPaymentId == key : l.PaymentTransactionId == key));
        result.ExistingLoanId = existing?.Id;
    }

    internal static async Task AttachDraftAsync(IPaymentDraftRepository drafts, string userId, string externalIdentifier, ShippingPayConfirmResultDto result)
    {
        var draftId = externalIdentifier[(externalIdentifier.LastIndexOf('|') + 1)..];
        if (draftId.Length == 0 || draftId.Length == 32)
            return;
        try
        {
            var draft = await drafts.GetByIdAsync(draftId);
            if (draft is not null && draft.UserId == userId)
            {
                result.Draft = draft.Draft;
                result.IsBatch = draft.IsBatch;
            }
        }
        catch
        {
            // בלי הפרטים השמורים הדפדפן עדיין יכול להשתמש בעותק שלו
        }
    }

    private static string Prefix(string userId) => $"order|{userId}|";

    public Task<ShippingPayStatusDto> GetStatusAsync(string userId) =>
        Task.FromResult(new ShippingPayStatusDto { Enabled = _cards.IsEnabled });

    public async Task<string> BeginAsync(string userId, ShippingPayStartDto dto)
    {
        if (!_cards.IsEnabled)
            throw new InvalidOperationException("תשלום באשראי אינו פעיל כרגע");
        if (dto.Amount <= 0)
            throw new ArgumentException("סכום התשלום אינו תקין");

        var user = await _users.GetByIdAsync(userId) ?? throw new KeyNotFoundException("המשתמשת לא נמצאה");
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var saveCard = dto.Consent ? "s1" : "s0";
        var draftId = await SaveDraftAsync(_drafts, userId, dto);

        return await _cards.BeginPaymentAsync(new CardPaymentRequest
        {
            Amount = dto.Amount,
            Description = Description,
            CustomerName = user.Name,
            Email = user.Email,
            Phone = user.Phone,
            CustomerExternalId = user.Id!,
            ExternalIdentifier = $"{Prefix(userId)}{saveCard}|{draftId}",
            RedirectUrl = $"{baseUrl}/order-paid",
            CancelUrl = $"{baseUrl}/",
            SavePaymentMethod = dto.Consent
        });
    }

    public async Task<ShippingPayConfirmResultDto> ConfirmAsync(string userId, ShippingPayConfirmDto dto)
    {
        var notPaid = new ShippingPayConfirmResultDto();
        if (!_cards.IsEnabled || !long.TryParse(dto.PaymentId, NumberStyles.None, CultureInfo.InvariantCulture, out var paymentId))
            return notPaid;

        var info = await _cards.GetPaymentAsync(paymentId);
        if (info is null || !info.Valid || string.IsNullOrEmpty(info.ExternalIdentifier)
            || !info.ExternalIdentifier.StartsWith(Prefix(userId), StringComparison.Ordinal))
            return notPaid;
        if (!string.IsNullOrEmpty(dto.ExternalIdentifier) && dto.ExternalIdentifier != info.ExternalIdentifier)
            return notPaid;

        if (info.ExternalIdentifier[Prefix(userId).Length..].StartsWith("s1|", StringComparison.Ordinal))
            await _users.UpdateShippingCardAsync(userId, DateTime.UtcNow);

        var result = new ShippingPayConfirmResultDto { Paid = true, PaymentId = paymentId.ToString(CultureInfo.InvariantCulture), Amount = info.Amount };
        await AttachExistingLoanAsync(_loans, userId, result, shipping: false);
        if (result.ExistingLoanId is null)
            await AttachDraftAsync(_drafts, userId, info.ExternalIdentifier, result);
        return result;
    }

    public async Task VerifyAsync(User payer, decimal expectedTotal, string? paymentId)
    {
        if (!_cards.IsEnabled)
            throw new InvalidOperationException("תשלום באשראי אינו פעיל כרגע - נא לבחור אמצעי תשלום אחר");
        if (!long.TryParse(paymentId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            throw new ArgumentException("יש להשלים תשלום באשראי לפני אישור ההזמנה");

        var info = await _cards.GetPaymentAsync(id);
        if (info is null || !info.Valid || string.IsNullOrEmpty(info.ExternalIdentifier)
            || !info.ExternalIdentifier.StartsWith(Prefix(payer.Id!), StringComparison.Ordinal))
            throw new InvalidOperationException("לא הצלחנו לאמת את התשלום באשראי");
        if (info.Amount + 0.01m < expectedTotal)
            throw new InvalidOperationException("הסכום ששולם נמוך מסכום ההזמנה - נא לנסות שוב");

        var key = paymentId!;
        var used = (await _loans.GetAllAsync()).Any(l => l.PaymentTransactionId == key || l.ShippingPaymentId == key);
        if (used)
            throw new InvalidOperationException("התשלום הזה כבר שימש להזמנה אחרת");
    }
}
