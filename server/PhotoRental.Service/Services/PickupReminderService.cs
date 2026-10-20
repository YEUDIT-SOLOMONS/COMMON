namespace PhotoRental.Service.Services;

using System.Collections.Concurrent;
using System.Globalization;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// תזכורות לפני קבלת הציוד:
// - ערב לפני (20:00), רק אם ההזמנה אושרה לפחות 36 שעות לפני תחילת ההשכרה: מייל לשוכרת ולמשכירה
// - שעה לפני איסוף עצמי: שיחה קצרה לשוכרת
// - שעתיים לפני האיסוף העצמי / תחילת חלון ההגעה של השליח: שיחה קצרה למשכירה
// כל תזכורת נשלחת פעם אחת (IMarkerRepository) ולא בשבת/חג
public class PickupReminderService : IPickupReminderService
{
    private static readonly TimeSpan EveningLead = TimeSpan.FromHours(36);
    private static readonly ConcurrentDictionary<string, (DateTime FetchedUtc, DateTime? WindowStart)> CourierWindows = new();

    private readonly ILoanRepository _loans;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly IMarkerRepository _markers;
    private readonly IEmailService _email;
    private readonly IOutboundCallService _calls;
    private readonly IShippingService _shipping;
    private readonly ClientOptions _client;

    public PickupReminderService(
        ILoanRepository loans,
        IItemRepository items,
        IUserRepository users,
        IMarkerRepository markers,
        IEmailService email,
        IOutboundCallService calls,
        IShippingService shipping,
        ClientOptions client)
    {
        _loans = loans;
        _items = items;
        _users = users;
        _markers = markers;
        _email = email;
        _calls = calls;
        _shipping = shipping;
        _client = client;
    }

    private static TimeSpan StartTimeOf(string? startTime) =>
        !string.IsNullOrWhiteSpace(startTime) && TimeSpan.TryParseExact(startTime, "hh\\:mm", CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : new TimeSpan(12, 0, 0);

    private static string FirstName(string? name) =>
        (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    public async Task ProcessAsync()
    {
        var now = IsraelTime.Now;
        if (!CallWindow.IsAllowed(now))
            return;

        var upcoming = (await _loans.GetByStatusAsync(LoanStatus.Active))
            .Where(l => !string.IsNullOrEmpty(l.Id) && l.CancelledAtUtc is null && l.PickedUpAt is null)
            .Select(l => (Loan: l, Start: l.StartDate.Date + StartTimeOf(l.StartTime)))
            .Where(x => x.Start > now && x.Start <= now.AddHours(30))
            .ToList();

        foreach (var (loan, start) in upcoming)
        {
            try
            {
                await ProcessLoanAsync(loan, start, now);
            }
            catch
            {
                // תקלה בהשכרה אחת לא עוצרת את השאר
            }
        }
    }

    private async Task ProcessLoanAsync(Loan loan, DateTime start, DateTime now)
    {
        var created = IsraelTime.FromUtc(loan.LoanDate);
        var lead = start - created;
        Item? item = null;
        User? renter = null;
        User? owner = null;
        async Task LoadAsync()
        {
            item ??= string.IsNullOrEmpty(loan.ItemCopyId) ? null : await _items.GetByIdAsync(loan.ItemCopyId);
            renter ??= string.IsNullOrEmpty(loan.BorrowerId) ? null : await _users.GetByIdAsync(loan.BorrowerId);
            owner ??= string.IsNullOrEmpty(item?.OwnerId) ? null : await _users.GetByIdAsync(item.OwnerId);
        }

        // ערב לפני
        var evening = start.Date.AddDays(-1).AddHours(20);
        if (lead >= EveningLead && now >= evening && await MarkOnceAsync($"pickupeve_{loan.Id}"))
        {
            await LoadAsync();
            await SendEveningEmailsAsync(loan, start, item, renter, owner);
        }

        var selfPickup = loan.DeliveryMethod == DeliveryMethod.SelfPickup;

        // שעה לפני איסוף עצמי - לשוכרת
        var renterCallAt = start.AddHours(-1);
        if (selfPickup && created <= renterCallAt.AddMinutes(-30) && now >= renterCallAt && now < start.AddMinutes(-10)
            && await MarkOnceAsync($"pickupcall_{loan.Id}"))
        {
            await LoadAsync();
            if (item is not null && renter is not null)
            {
                var street = item.Street ?? ItemService.StreetWithoutHouseNumber(item.Address);
                var place = string.IsNullOrWhiteSpace(street) ? $"ב{item.City}" : $"מרחוב {street} ב{item.City}";
                var message = loan.IsLocation
                    ? $"שלום {renter.Name}. זוהי תזכורת אוטומטית מקומון. בעוד כשעה, בשעה {start:HH:mm}, מתחיל הצילום ב{item.Name} {place}. תודה."
                    : $"שלום {renter.Name}. זוהי תזכורת אוטומטית מקומון. בעוד כשעה, בשעה {start:HH:mm}, איסוף {item.Name} {place}. תודה.";
                await _calls.CallForLoanAsync(CallKind.PickupReminder, loan, item, renter, message: message);
            }
        }

        // שעתיים לפני - למשכירה (באיסוף עצמי: לפני שעת האיסוף; במשלוח: לפני תחילת חלון ההגעה של השליח)
        DateTime ownerCallAt;
        string ownerMessageTail;
        if (selfPickup)
        {
            ownerCallAt = start.AddHours(-2);
            ownerMessageTail = null!;
        }
        else
        {
            var window = await CourierWindowStartAsync(loan);
            ownerCallAt = (window ?? start.AddHours(-2)).AddHours(-2);
            ownerMessageTail = window is null ? "בשעות הקרובות" : $"בעוד כשעתיים, החל מהשעה {window:HH:mm},";
        }

        if (created <= ownerCallAt.AddMinutes(-30) && now >= ownerCallAt && now < ownerCallAt.AddHours(1)
            && await MarkOnceAsync($"pickupowner_{loan.Id}"))
        {
            await LoadAsync();
            if (item is not null && owner is not null)
            {
                var message = loan.IsLocation
                    ? $"שלום {owner.Name}. זוהי תזכורת אוטומטית מקומון. בעוד כשעתיים, בשעה {start:HH:mm}, מגיעה צלמת לצילום ב{item.Name}. נא לוודא שהמקום מוכן. תודה."
                    : selfPickup
                    ? $"שלום {owner.Name}. זוהי תזכורת אוטומטית מקומון. בעוד כשעתיים, בשעה {start:HH:mm}, השוכרת תגיע לאסוף את {item.Name}. נא לוודא שהציוד מוכן. תודה."
                    : $"שלום {owner.Name}. זוהי תזכורת אוטומטית מקומון. {ownerMessageTail} יגיע שליח לאסוף את {item.Name} לקראת ההשכרה. נא להכין את הציוד ארוז ומוכן. תודה.";
                await _calls.CallForLoanAsync(CallKind.PickupOwnerReminder, loan, item, renter, message: message);
            }
        }
    }

    private async Task<bool> MarkOnceAsync(string key)
    {
        if (await _markers.ExistsAsync(key))
            return false;
        await _markers.MarkAsync(key);
        return true;
    }

    // תחילת חלון ההגעה של השליח לפי GetPackage (נשלף לכל היותר פעם בשעה לכל השכרה)
    private async Task<DateTime?> CourierWindowStartAsync(Loan loan)
    {
        if (string.IsNullOrEmpty(loan.OutboundProviderId))
            return null;
        if (CourierWindows.TryGetValue(loan.Id!, out var cached) && DateTime.UtcNow - cached.FetchedUtc < TimeSpan.FromHours(1))
            return cached.WindowStart;

        DateTime? windowStart = null;
        try
        {
            var status = await _shipping.GetStatusAsync(loan.OutboundProviderId);
            var range = status?.EtaTimeRange?.Split('-', StringSplitOptions.TrimEntries).FirstOrDefault();
            if (status?.EtaDate is DateTime date && TimeSpan.TryParse(range, CultureInfo.InvariantCulture, out var from))
                windowStart = date.Date + from;
        }
        catch
        {
            // בלי חלון ידוע - שעתיים לפני תחילת ההשכרה פחות שעתיים
        }

        CourierWindows[loan.Id!] = (DateTime.UtcNow, windowStart);
        return windowStart;
    }

    private async Task SendEveningEmailsAsync(Loan loan, DateTime start, Item? item, User? renter, User? owner)
    {
        if (!_email.IsEnabled || item is null)
            return;

        string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var link = $"{baseUrl}/order/{Uri.EscapeDataString(loan.Id!)}";
        const string button = "display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none";
        var when = $"מחר ({start:dd/MM}) בשעה {start:HH:mm}";
        var selfPickup = loan.DeliveryMethod == DeliveryMethod.SelfPickup;
        var street = item.Street ?? ItemService.StreetWithoutHouseNumber(item.Address);

        if (renter is not null && !string.IsNullOrWhiteSpace(renter.Email))
        {
            var body = loan.IsLocation
                ? $"<p>תזכורת: {when} מתחיל הצילום ב<strong>{H(item.Name)}</strong>{(string.IsNullOrWhiteSpace(street) ? "" : $", רחוב {H(street)}")}, {H(item.City)}, עד השעה {loan.ExpectedReturnDate:HH:mm}.</p><p>הכתובת המלאה ופרטי ההזמנה בקישור:</p>"
                : selfPickup
                ? $"<p>תזכורת: {when} איסוף <strong>{H(LoanText.Name(item, loan))}</strong>{(string.IsNullOrWhiteSpace(street) ? "" : $" מרחוב {H(street)}")}, {H(item.City)}.</p><p>הכתובת המלאה ופרטי ההזמנה בקישור:</p>"
                : $"<p>תזכורת: <strong>{H(LoanText.Name(item, loan))}</strong> יגיע אלייך עם שליח עד {when}.</p><p>פרטי ההזמנה ומעקב המשלוח בקישור:</p>";
            var html = $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>שלום {H(NameText.FirstName(renter.Name))},</p>{body}" +
                       $"<p><a href=\"{link}\" style=\"{button}\"><strong>לפרטי ההזמנה</strong></a></p><p>בהצלחה,<br/>common</p></div>";
            try { await _email.SendAsync(renter.Email, renter.Name, $"תזכורת: מחר {item.Name} - common", html); } catch { }
        }

        if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
        {
            var body = loan.IsLocation
                ? $"<p>תזכורת: {when} מגיעה צלמת לצילום ב<strong>{H(item.Name)}</strong>, עד השעה {loan.ExpectedReturnDate:HH:mm}.</p><p>נא לוודא שהמקום נקי ומוכן.</p>"
                : selfPickup
                ? $"<p>תזכורת: {when} תגיע השוכרת לאסוף את <strong>{H(LoanText.Name(item, loan))}</strong>.</p><p>נא לוודא שהציוד טעון, נקי ומוכן.</p>"
                : $"<p>תזכורת: מחר יגיע שליח לאסוף את <strong>{H(LoanText.Name(item, loan))}</strong> לקראת ההשכרה שמתחילה {when}.</p><p>נא להכין את הציוד ארוז ומוכן. נתקשר אלייך שעתיים לפני הגעת השליח.</p>";
            var html = $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>שלום {H(NameText.FirstName(owner.Name))},</p>{body}<p>תודה,<br/>common</p></div>";
            try { await _email.SendAsync(owner.Email, owner.Name, $"תזכורת: מחר השכרת {item.Name} - common", html); } catch { }
        }
    }
}
