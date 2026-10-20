namespace PhotoRental.Service.Services;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// פריט שלא הושכר 6 שבועות (42 יום): נשלח לבעלים מייל "האם הפריט עדיין תקין וזמין להשכרה?" עם שני קישורים:
// "זמין" (מאפס את מניין 6 השבועות) ו"להסרה מהאתר". הקישורים נפתחים בלי התחברות ומוגנים בטוקן סודי לפריט.
// מניין הזמן מתחיל מהמאוחר מבין: ההשכרה האחרונה, הוספת הפריט, ואישור "זמין" האחרון. בלי תשובה - המייל
// נשלח שוב רק אחרי עוד 6 שבועות. פריטים ישנים (בלי תאריך הוספה) מקבלים חותמת בבדיקה הראשונה, וממנה מתחיל המניין.
public class StaleItemService : IStaleItemService
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(42);

    private readonly IItemRepository _itemRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly ClientOptions _clientOptions;

    public StaleItemService(
        IItemRepository itemRepository,
        ILoanRepository loanRepository,
        IUserRepository userRepository,
        IEmailService emailService,
        ClientOptions clientOptions)
    {
        _itemRepository = itemRepository;
        _loanRepository = loanRepository;
        _userRepository = userRepository;
        _emailService = emailService;
        _clientOptions = clientOptions;
    }

    public async Task<int> SendNoticesAsync()
    {
        if (!_emailService.IsEnabled)
            return 0;

        var now = DateTime.UtcNow;
        var items = await _itemRepository.GetAllAsync();
        var loans = await _loanRepository.GetAllAsync();

        var lastRentalByItem = loans
            .Where(l => !string.IsNullOrEmpty(l.ItemCopyId))
            .GroupBy(l => l.ItemCopyId!)
            .ToDictionary(g => g.Key, g => g.Max(l => l.LoanDate > l.StartDate ? l.LoanDate : l.StartDate));
        var itemsWithActiveLoan = loans
            .Where(l => l.Status == LoanStatus.Active && !string.IsNullOrEmpty(l.ItemCopyId))
            .Select(l => l.ItemCopyId!)
            .ToHashSet();

        var sent = 0;
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.OwnerId))
                continue;
            if (item.DeletedAtUtc is not null || item.Status != CopyStatus.Available || itemsWithActiveLoan.Contains(item.Id))
                continue;

            // פריט ישן בלי תאריך הוספה: מסמנים עכשיו, והמניין מתחיל מכאן
            if (item.CreatedAtUtc is null)
            {
                item.CreatedAtUtc = now;
                await _itemRepository.PatchStaleStateAsync(item.Id, item);
                continue;
            }

            var reference = item.CreatedAtUtc.Value;
            if (item.AvailabilityConfirmedAtUtc is { } confirmed && confirmed > reference) reference = confirmed;
            if (lastRentalByItem.TryGetValue(item.Id, out var lastRental) && lastRental > reference) reference = lastRental;

            if (now - reference < StaleAfter)
                continue;
            if (item.StaleNoticeSentAtUtc is { } lastNotice && now - lastNotice < StaleAfter)
                continue;

            var owner = await _userRepository.GetByIdAsync(item.OwnerId);
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
                continue;

            item.StaleActionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            item.StaleNoticeSentAtUtc = now;
            await _itemRepository.PatchStaleStateAsync(item.Id, item);

            try
            {
                await _emailService.SendAsync(owner.Email, owner.Name, $"האם {item.Name} עדיין זמין להשכרה? - common", BuildEmail(item, owner));
                sent++;
            }
            catch
            {
                // נשלח שוב בסבב הבא: מבטלים את סימון השליחה
                item.StaleNoticeSentAtUtc = null;
                await _itemRepository.PatchStaleStateAsync(item.Id, item);
            }
        }

        return sent;
    }

    private string BuildEmail(Item item, User owner)
    {
        static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

        var baseUrl = _clientOptions.BaseUrl.TrimEnd('/');
        var availableLink = $"{baseUrl}/item-action/{item.Id}?t={item.StaleActionToken}&a=available";
        var removeLink = $"{baseUrl}/item-action/{item.Id}?t={item.StaleActionToken}&a=remove";

        return "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
               $"<p>שלום {H(NameText.FirstName(owner.Name))},</p>" +
               $"<p>ראינו שהפריט <strong>{H(item.Name)}</strong> לא הושכר ב-6 השבועות האחרונים.</p>" +
               "<p><strong>האם הפריט עדיין תקין וזמין להשכרה?</strong></p>" +
               $"<p><a href=\"{H(availableLink)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 22px;color:#000;text-decoration:none\"><strong>כן, הפריט זמין</strong></a></p>" +
               $"<p><a href=\"{H(removeLink)}\" style=\"display:inline-block;background:#e89c9c;border:1px solid #000;border-radius:20px;padding:10px 22px;color:#000;text-decoration:none\"><strong>להסרה מהאתר</strong></a></p>" +
               "</div>";
    }

    public async Task<bool> ValidateTokenAsync(string itemId, string? token)
    {
        if (string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(token))
            return false;

        var item = await _itemRepository.GetByIdAsync(itemId);
        if (item is null || string.IsNullOrEmpty(item.StaleActionToken))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(item.StaleActionToken),
            Encoding.UTF8.GetBytes(token));
    }

    public async Task<ItemActionDto?> GetActionViewAsync(string itemId)
    {
        var item = await _itemRepository.GetByIdAsync(itemId);
        if (item is null) return null;

        return new ItemActionDto
        {
            ItemName = item.Name,
            ImageDataUrl = PhotoRental.Core.Helpers.ImageLinks.Thumb(item.Id, item.ImageDataUrl),
            HasActiveLoan = await HasActiveLoanAsync(itemId)
        };
    }

    public async Task ConfirmAvailableAsync(string itemId)
    {
        var item = await _itemRepository.GetByIdAsync(itemId)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");

        item.AvailabilityConfirmedAtUtc = DateTime.UtcNow;
        await _itemRepository.PatchStaleStateAsync(itemId, item);
    }

    public async Task RemoveAsync(string itemId)
    {
        var item = await _itemRepository.GetByIdAsync(itemId)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");

        if (await HasActiveLoanAsync(itemId))
            throw new InvalidOperationException("לא ניתן להסיר פריט שמושכר כרגע - אפשר להסיר אותו אחרי שיוחזר");

        // מחיקה רכה - היסטוריית ההשכרות של הפריט נשמרת
        await _itemRepository.SoftDeleteAsync(itemId, DateTime.UtcNow);
    }

    private async Task<bool> HasActiveLoanAsync(string itemId) =>
        (await _loanRepository.GetAllAsync()).Any(l => l.ItemCopyId == itemId && l.Status == LoanStatus.Active);
}
