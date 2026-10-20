namespace PhotoRental.Service.Services;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class ReviewService : IReviewService
{
    public const string RenterRole = "Renter";
    public const string OwnerRole = "Owner";
    private static readonly TimeSpan ReviewWindow = TimeSpan.FromDays(30);

    // ברשימה באזור האישי: שבוע, אחריו תזכורת במייל לשוכרת, ואחרי 4 ימים נוספים יורד מהרשימה
    private static readonly TimeSpan ReminderAfter = TimeSpan.FromDays(7);
    private static readonly TimeSpan PendingWindow = TimeSpan.FromDays(11);

    private readonly IReviewRepository _reviews;
    private readonly ILoanRepository _loans;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly IMarkerRepository _markers;
    private readonly IEmailService _email;
    private readonly ClientOptions _client;

    public ReviewService(
        IReviewRepository reviews,
        ILoanRepository loans,
        IItemRepository items,
        IUserRepository users,
        IMarkerRepository markers,
        IEmailService email,
        ClientOptions client)
    {
        _reviews = reviews;
        _loans = loans;
        _items = items;
        _users = users;
        _markers = markers;
        _email = email;
        _client = client;
    }

    private static string FirstName(string? name) =>
        (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

    private static DateTime? ReturnedLocal(Loan loan) =>
        loan.ActualReturnDate is null ? null : IsraelTime.FromUtc(loan.ActualReturnDate.Value);

    private static bool InWindow(Loan loan) =>
        loan.Status == LoanStatus.Returned && ReturnedLocal(loan) is DateTime returned
        && IsraelTime.Now - returned <= ReviewWindow;

    private static bool InPendingWindow(Loan loan) =>
        loan.Status == LoanStatus.Returned && ReturnedLocal(loan) is DateTime returned
        && IsraelTime.Now - returned <= PendingWindow;

    private static string DismissKey(string loanId, string role) => $"reviewdismiss_{loanId}_{role}";

    private async Task<bool> IsClosedAsync(string loanId, string role) =>
        await _reviews.GetAsync(loanId, role) is not null || await _markers.ExistsAsync(DismissKey(loanId, role));

    public async Task<ItemReviewsDto> GetItemReviewsAsync(string itemId)
    {
        var visible = (await _reviews.GetByItemAsync(itemId))
            .Where(r => r.ByRole == RenterRole && !r.Hidden)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToList();
        return new ItemReviewsDto
        {
            Count = visible.Count,
            Average = visible.Count == 0 ? 0 : Math.Round(visible.Average(r => r.Stars), 1),
            Reviews = visible.Take(10).Select(r => new PublicReviewDto
            {
                Stars = r.Stars,
                Text = r.Text,
                AuthorFirstName = FirstName(r.AuthorName),
                CreatedAtUtc = r.CreatedAtUtc
            }).ToList()
        };
    }

    public async Task<Dictionary<string, RatingSummaryDto>> GetSummaryAsync() =>
        (await _reviews.GetAllAsync())
            .Where(r => r.ByRole == RenterRole && !r.Hidden && !string.IsNullOrEmpty(r.ItemId))
            .GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => new RatingSummaryDto { Count = g.Count(), Average = Math.Round(g.Average(r => r.Stars), 1) });

    public async Task<IReadOnlyList<PendingReviewDto>> GetPendingAsync(string userId)
    {
        var pending = new List<PendingReviewDto>();

        var renterLoans = (await _loans.GetByBorrowerIdAsync(userId)).Where(InPendingWindow).ToList();
        var renterItems = await _items.GetByIdsAsync(renterLoans.Select(l => l.ItemCopyId!).Where(id => !string.IsNullOrEmpty(id)));
        foreach (var loan in renterLoans)
        {
            if (string.IsNullOrEmpty(loan.Id) || await IsClosedAsync(loan.Id, RenterRole))
                continue;
            var item = string.IsNullOrEmpty(loan.ItemCopyId) ? null : renterItems.GetValueOrDefault(loan.ItemCopyId);
            pending.Add(new PendingReviewDto
            {
                LoanId = loan.Id,
                Role = RenterRole,
                ItemName = item?.Name ?? string.Empty,
                ReturnedAt = ReturnedLocal(loan)
            });
        }

        return pending.OrderByDescending(p => p.ReturnedAt).ToList();
    }

    public async Task SubmitAsync(string userId, string loanId, ReviewSubmitDto dto)
    {
        if (dto.Stars is < 1 or > 5)
            throw new ArgumentException("יש לבחור דירוג בין 1 ל-5 כוכבים");
        var text = string.IsNullOrWhiteSpace(dto.Text) ? null : dto.Text.Trim();
        if (text is { Length: > 500 })
            throw new ArgumentException("הטקסט ארוך מדי (עד 500 תווים)");
        var note = string.IsNullOrWhiteSpace(dto.ThankYouNote) ? null : dto.ThankYouNote.Trim();
        if (note is { Length: > 300 })
            throw new ArgumentException("המילה הטובה ארוכה מדי (עד 300 תווים)");

        var loan = await _loans.GetByIdAsync(loanId) ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");
        if (loan.Status != LoanStatus.Returned)
            throw new InvalidOperationException("אפשר לדרג רק אחרי שההשכרה הסתיימה והציוד הוחזר");
        if (!InWindow(loan))
            throw new InvalidOperationException("עברו יותר מ-30 יום מסיום ההשכרה - כבר אי אפשר לדרג אותה");

        var item = string.IsNullOrEmpty(loan.ItemCopyId) ? null : await _items.GetByIdAsync(loan.ItemCopyId);
        var role = loan.BorrowerId == userId ? RenterRole
            : throw new UnauthorizedAccessException("אפשר לדרג רק השכרות שלך");

        if (await _reviews.GetAsync(loanId, role) is not null)
            throw new InvalidOperationException("כבר דירגת את ההשכרה הזו. תודה!");

        var author = await _users.GetByIdAsync(userId);
        await _reviews.UpsertAsync(new Review
        {
            LoanId = loanId,
            ByRole = role,
            ItemId = loan.ItemCopyId ?? string.Empty,
            ItemName = item?.Name ?? string.Empty,
            OwnerId = item?.OwnerId ?? string.Empty,
            RenterId = loan.BorrowerId ?? string.Empty,
            AuthorName = author?.Name ?? string.Empty,
            Stars = dto.Stars,
            Text = text,
            ThankYouNote = role == RenterRole ? note : null,
            CreatedAtUtc = DateTime.UtcNow
        });

        if (role == RenterRole && note is not null)
            await SendThankYouNoteAsync(item, author, note);
    }

    private async Task SendThankYouNoteAsync(Item? item, User? author, string note)
    {
        if (!_email.IsEnabled || string.IsNullOrEmpty(item?.OwnerId))
            return;
        try
        {
            var owner = await _users.GetByIdAsync(item.OwnerId);
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
                return;
            string H(string? t) => System.Net.WebUtility.HtmlEncode(t ?? string.Empty);
            var baseUrl = _client.BaseUrl.TrimEnd('/');
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>שלום {H(FirstName(owner.Name))},</p>" +
                       $"<p>{H(FirstName(author?.Name))} שכרה ממך את <strong>{H(item.Name)}</strong> והשאירה לך מילה טובה:</p>" +
                       $"<div style=\"background:#fff0cf;border:1px solid #000;border-radius:16px;padding:14px 16px;margin:12px 0;font-size:16px\">{H(note)}</div>" +
                       $"<p>כל המילים הטובות שקיבלת שמורות <a href=\"{baseUrl}/profile\">באזור האישי</a>.</p>" +
                       "<p>common</p></div>";
            await _email.SendAsync(owner.Email, owner.Name, $"קיבלת מילה טובה מ{FirstName(author?.Name)} - common", html);
        }
        catch
        {
            // מייל שלא נשלח לא מבטל את הדירוג
        }
    }

    public async Task<IReadOnlyList<ThankYouNoteDto>> GetThankYouNotesAsync(string ownerId) =>
        (await _reviews.GetByOwnerAsync(ownerId))
            .Where(r => r.ByRole == RenterRole && !string.IsNullOrWhiteSpace(r.ThankYouNote))
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new ThankYouNoteDto
            {
                Text = r.ThankYouNote!,
                FromFirstName = FirstName(r.AuthorName),
                ItemName = r.ItemName,
                CreatedAtUtc = r.CreatedAtUtc
            })
            .ToList();

    public async Task DismissAsync(string userId, string loanId)
    {
        var loan = await _loans.GetByIdAsync(loanId) ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");
        var item = string.IsNullOrEmpty(loan.ItemCopyId) ? null : await _items.GetByIdAsync(loan.ItemCopyId);
        var role = loan.BorrowerId == userId ? RenterRole
            : item?.OwnerId == userId ? OwnerRole
            : throw new UnauthorizedAccessException("אפשר להסיר רק השכרות שלך");
        await _markers.MarkAsync(DismissKey(loanId, role));
    }

    private async Task<bool> IsFirstReturnedAsOwnerAsync(string ownerId)
    {
        var itemIds = (await _items.GetByOwnerIdAsync(ownerId)).Select(i => i.Id).ToHashSet();
        return (await _loans.GetByStatusAsync(LoanStatus.Returned)).Count(l => itemIds.Contains(l.ItemCopyId)) == 1;
    }

    public async Task<IReadOnlyList<AdminReviewDto>> GetAllForAdminAsync()
    {
        var names = (await _users.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!, u => u.Name);
        return (await _reviews.GetAllAsync())
            .OrderByDescending(r => r.CreatedAtUtc)
            .Select(r => new AdminReviewDto
            {
                Id = r.Id,
                LoanId = r.LoanId,
                ByRole = r.ByRole,
                ItemName = r.ItemName,
                AuthorName = r.AuthorName,
                SubjectName = names.GetValueOrDefault(r.ByRole == RenterRole ? r.OwnerId : r.RenterId) ?? string.Empty,
                Stars = r.Stars,
                Text = r.Text,
                ThankYouNote = r.ThankYouNote,
                CreatedAtUtc = r.CreatedAtUtc,
                Hidden = r.Hidden
            })
            .ToList();
    }

    public Task SetHiddenAsync(string reviewId, bool hidden) => _reviews.SetHiddenAsync(reviewId, hidden);

    public async Task SendReviewRequestsAsync()
    {
        if (!_email.IsEnabled)
            return;
        var now = IsraelTime.Now;
        if (!CallWindow.IsAllowed(now) || now.Hour < 10 || now.Hour >= 21)
            return;

        var due = (await _loans.GetByStatusAsync(LoanStatus.Returned))
            .Where(l => !string.IsNullOrEmpty(l.Id) && l.ActualReturnDate is not null
                        && now.Date > ReturnedLocal(l)!.Value.Date && now - ReturnedLocal(l)!.Value <= TimeSpan.FromDays(3))
            .ToList();

        var baseUrl = _client.BaseUrl.TrimEnd('/');
        foreach (var loan in due)
        {
            // השוכרת שהחזירה בזמן כבר קיבלה את בקשת הדירוג במייל התודה, ולכן מקבלת מייל נפרד רק אם לא קיבלה אותו
            var renterKey = $"reviewreq_renter_{loan.Id}";
            var ownerKey = $"reviewreq_owner_{loan.Id}";
            var askRenter = !await _markers.ExistsAsync(renterKey);
            var askOwner = !await _markers.ExistsAsync(ownerKey);
            if (!askRenter && !askOwner)
                continue;
            if (askRenter) await _markers.MarkAsync(renterKey);
            if (askOwner) await _markers.MarkAsync(ownerKey);

            try
            {
                var item = string.IsNullOrEmpty(loan.ItemCopyId) ? null : await _items.GetByIdAsync(loan.ItemCopyId);
                var renter = string.IsNullOrEmpty(loan.BorrowerId) ? null : await _users.GetByIdAsync(loan.BorrowerId);
                var owner = string.IsNullOrEmpty(item?.OwnerId) ? null : await _users.GetByIdAsync(item.OwnerId);
                var link = $"{baseUrl}/profile?review={Uri.EscapeDataString(loan.Id!)}";
                string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
                const string button = "display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none";

                if (askRenter && renter is not null && !string.IsNullOrWhiteSpace(renter.Email) && await _reviews.GetAsync(loan.Id!, RenterRole) is null)
                {
                    var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                               $"<p>שלום {H(NameText.FirstName(renter.Name))},</p>" +
                               $"<p>איך היה עם <strong>{H(item?.Name)}</strong>? נשמח לדירוג קצר של הפריט.</p>" +
                               "<p>הדירוג עוזר לצלמות אחרות בקהילה לבחור ציוד, ולוקח פחות מדקה.</p>" +
                               $"<p><a href=\"{link}\" style=\"{button}\"><strong>לדירוג ההשכרה</strong></a></p>" +
                               "<p>תודה,<br/>common</p></div>";
                    await _email.SendAsync(renter.Email, renter.Name, "איך היה? דרגי את ההשכרה - common", html);
                }

                if (askOwner && owner is not null && !string.IsNullOrWhiteSpace(owner.Email) && await IsFirstReturnedAsOwnerAsync(owner.Id!))
                {
                    var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                               $"<p>שלום {H(NameText.FirstName(owner.Name))},</p>" +
                               $"<p>ההשכרה הראשונה שלך, <strong>{H(item?.Name)}</strong>, הסתיימה.</p>" +
                               LoanService.FirstRentalQuestionHtml(asOwner: true, baseUrl) +
                               "<p>תודה,<br/>common</p></div>";
                    await _email.SendAsync(owner.Email, owner.Name, "ההשכרה הראשונה שלך הסתיימה - common", html);
                }
            }
            catch
            {
                // מייל שלא נשלח לא עוצר את שאר הבקשות
            }
        }

        // שבוע אחרי ההחזרה ועדיין לא דורג (ולא הוסר) - תזכורת אחת לשוכרת
        var remind = (await _loans.GetByStatusAsync(LoanStatus.Returned))
            .Where(l => !string.IsNullOrEmpty(l.Id) && !string.IsNullOrEmpty(l.BorrowerId) && ReturnedLocal(l) is DateTime r
                        && now - r >= ReminderAfter && now - r < PendingWindow)
            .ToList();
        foreach (var loan in remind)
        {
            var key = $"reviewremind_renter_{loan.Id}";
            if (await _markers.ExistsAsync(key) || await IsClosedAsync(loan.Id!, RenterRole))
                continue;
            await _markers.MarkAsync(key);

            try
            {
                var renter = await _users.GetByIdAsync(loan.BorrowerId!);
                if (renter is null || string.IsNullOrWhiteSpace(renter.Email))
                    continue;
                var item = string.IsNullOrEmpty(loan.ItemCopyId) ? null : await _items.GetByIdAsync(loan.ItemCopyId);
                string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
                var link = $"{baseUrl}/profile?review={Uri.EscapeDataString(loan.Id!)}";
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {H(NameText.FirstName(renter.Name))},</p>" +
                           $"<p>עוד לא דירגת את ההשכרה של <strong>{H(item?.Name)}</strong>. נשמח לדירוג קצר - זה לוקח פחות מדקה ועוזר לצלמות אחרות בקהילה.</p>" +
                           $"<p><a href=\"{link}\" style=\"display:inline-block;background:#fff0cf;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>★ לדירוג ההשכרה</strong></a></p>" +
                           "<p>הבקשה תוסר מהרשימה שלך בעוד 4 ימים.</p>" +
                           "<p>תודה,<br/>common</p></div>";
                await _email.SendAsync(renter.Email, renter.Name, "תזכורת: איך היה? - common", html);
            }
            catch
            {
                // מייל שלא נשלח לא עוצר את שאר התזכורות
            }
        }
    }
}
