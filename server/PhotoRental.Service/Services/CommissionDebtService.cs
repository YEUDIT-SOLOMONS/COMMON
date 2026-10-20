namespace PhotoRental.Service.Services;

using System.Net;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

public class CommissionDebtService : ICommissionDebtService
{
    private static readonly TimeSpan DueAfter = TimeSpan.FromDays(14);
    private static readonly TimeSpan WarnAfter = TimeSpan.FromDays(7);
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);

    private static readonly string[] HebrewMonths =
    {
        "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני", "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר"
    };

    // מטמון משותף לכל הבקשות: הקטלוג נשאל על חסומות בכל טעינה
    private static HashSet<string> _blockedCache = new();
    private static DateTime _blockedCachedAtUtc = DateTime.MinValue;

    private readonly ICommissionStatementRepository _statements;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly ClientOptions _client;

    public CommissionDebtService(
        ICommissionStatementRepository statements,
        IUserRepository users,
        IEmailService email,
        ClientOptions client)
    {
        _statements = statements;
        _users = users;
        _email = email;
        _client = client;
    }

    // אחרי שינוי מצב תשלום - מרעננים את המטמון מיד (נקרא מ-CommissionService)
    public static void InvalidateCache() => _blockedCachedAtUtc = DateTime.MinValue;

    // דוחות שנשלחו ולא שולמו
    private static bool IsOpen(CommissionStatement s) => s.SentAtUtc is not null && !s.Paid;

    public async Task<HashSet<string>> GetBlockedOwnerIdsAsync()
    {
        if (DateTime.UtcNow - _blockedCachedAtUtc < CacheFor)
            return _blockedCache;

        var now = DateTime.UtcNow;
        var blocked = (await _statements.GetAllAsync())
            .Where(s => IsOpen(s) && now - s.SentAtUtc!.Value >= DueAfter)
            .Select(s => s.OwnerId)
            .ToHashSet();

        _blockedCache = blocked;
        _blockedCachedAtUtc = now;
        return blocked;
    }

    public async Task<OwnerDebtStatus> GetStatusAsync(string ownerId)
    {
        var now = DateTime.UtcNow;
        var oldest = (await _statements.GetAllAsync())
            .Where(s => s.OwnerId == ownerId && IsOpen(s))
            .OrderBy(s => s.SentAtUtc)
            .FirstOrDefault();
        if (oldest is null)
            return new OwnerDebtStatus();

        var age = now - oldest.SentAtUtc!.Value;
        return new OwnerDebtStatus
        {
            State = age >= DueAfter ? "blocked" : "warning",
            Month = oldest.Month,
            MonthLabel = MonthLabel(oldest.Month),
            DaysLeft = Math.Max(0, (int)Math.Ceiling((DueAfter - age).TotalDays)),
            PayUrl = PayUrl(oldest)
        };
    }

    public async Task ProcessNoticesAsync()
    {
        if (!_email.IsEnabled)
            return;

        var now = DateTime.UtcNow;
        foreach (var statement in (await _statements.GetAllAsync()).Where(IsOpen))
        {
            var age = now - statement.SentAtUtc!.Value;
            var needsBlocked = age >= DueAfter && statement.DebtBlockedNoticeSentAtUtc is null;
            var needsWarning = age >= WarnAfter && age < DueAfter && statement.DebtWarningSentAtUtc is null;
            if (!needsBlocked && !needsWarning)
                continue;

            var owner = await _users.GetByIdAsync(statement.OwnerId);
            if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
                continue;

            try
            {
                await SendNoticeAsync(owner, statement, blocked: needsBlocked);
                if (needsBlocked) statement.DebtBlockedNoticeSentAtUtc = now;
                else statement.DebtWarningSentAtUtc = now;
                await _statements.UpsertAsync(statement);
            }
            catch
            {
                // נסיון חוזר בסבב הבא
            }
        }
    }

    private async Task SendNoticeAsync(User owner, CommissionStatement statement, bool blocked)
    {
        string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var monthLabel = MonthLabel(statement.Month);
        var payUrl = PayUrl(statement);
        var button = $"<p><a href=\"{H(payUrl)}\" style=\"display:inline-block;background:#e89c9c;color:#000;border:1.5px solid #000;border-radius:999px;padding:9px 20px;text-decoration:none;font-weight:bold\">לתשלום באשראי - לחצי כאן</a></p>";

        string subject;
        string body;
        if (blocked)
        {
            subject = "דוח העמלה לא שולם - הוספת פריטים והצגתם בקטלוג הושעו - common";
            body = $"<p>עברו 14 ימים מאז שנשלח אלייך דוח עמלת השירות לחודש <strong>{H(monthLabel)}</strong>, והוא עדיין לא שולם.</p>" +
                   "<p><strong>עד לסילוק החוב לא ניתן להוסיף פריטים חדשים, והפריטים שלך מוסתרים זמנית מהקטלוג.</strong> השכרות שכבר נקבעו ממשיכות כרגיל.</p>" +
                   "<p>מיד אחרי התשלום הכול חוזר לפעול אוטומטית.</p>";
        }
        else
        {
            subject = "תזכורת: דוח העמלה טרם שולם - common";
            body = $"<p>עברו 7 ימים מאז שנשלח אלייך דוח עמלת השירות לחודש <strong>{H(monthLabel)}</strong>, והוא עדיין לא שולם.</p>";
        }

        var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                   $"<p>שלום {H(NameText.FirstName(owner.Name))},</p>" + body + button +
                   "<p>אפשר לשלם גם בהעברה בנקאית (הפרטים בדוח שנשלח אלייך). אם כבר שילמת - אפשר להשיב למייל הזה ונעדכן.</p>" +
                   "<p>common</p></div>";
        await _email.SendAsync(owner.Email, owner.Name, subject, html);
    }

    private string PayUrl(CommissionStatement statement) =>
        $"{_client.BaseUrl.TrimEnd('/')}/commission-pay?o={Uri.EscapeDataString(statement.OwnerId)}&m={Uri.EscapeDataString(statement.Month)}&t={statement.ProofToken}";

    private static string MonthLabel(string month)
    {
        var parts = month.Split('-');
        return parts.Length == 2 && int.TryParse(parts[1], out var m) && m is >= 1 and <= 12
            ? $"{HebrewMonths[m - 1]} {parts[0]}"
            : month;
    }
}
