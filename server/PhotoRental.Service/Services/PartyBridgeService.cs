namespace PhotoRental.Service.Services;

using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// שיחה מתווכת בין שוכרת למשכירה דרך מספר common אחד: מי שמחייגת מזוהה לפי המספר שרשום בחשבון, ומחוברת לצד השני
// של השכרה פעילה. אף צד לא רואה את המספר של השנייה
public class PartyBridgeService : IPartyBridgeService
{
    private const string ContactHint = "אפשר לפנות להנהלת האתר בדף צור קשר.";

    private readonly ILoanRepository _loans;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly ICallLogRepository _logs;
    private readonly TwilioOptions _twilio;
    private readonly IReminderCallService _calls;

    public PartyBridgeService(
        ILoanRepository loans, IItemRepository items, IUserRepository users, ICallLogRepository logs, TwilioOptions twilio,
        IReminderCallService calls)
    {
        _calls = calls;
        _loans = loans;
        _items = items;
        _users = users;
        _logs = logs;
        _twilio = twilio;
    }

    public string? CommonNumber => string.IsNullOrWhiteSpace(_twilio.CommonNumber) ? null : _twilio.CommonNumber.Trim();

    public bool CallbackEnabled => _calls.IsEnabled && !string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl);

    private sealed record Candidate(Loan Loan, Item Item, User Counterpart, bool CallerIsRenter);

    private static bool IsPaid(Loan loan) =>
        loan.PaymentStatus == PaymentStatus.Paid || loan.PaymentMethod is PaymentMethod.BankTransfer or PaymentMethod.Cash;

    // לחיצה באתר: מחייגים לטלפון הרשום של מי שלחצה, וכשהיא עונה מחברים אותה לצד השני
    public async Task StartCallbackAsync(string userId, string loanId)
    {
        if (!CallbackEnabled)
            throw new InvalidOperationException("השיחה אינה זמינה כרגע");
        var blocked = CheckAllowed();
        if (blocked is not null)
            throw new InvalidOperationException(blocked);

        var loan = await _loans.GetByIdAsync(loanId) ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");
        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
        if (item is null || string.IsNullOrEmpty(item.OwnerId) || string.IsNullOrEmpty(loan.BorrowerId))
            throw new KeyNotFoundException("ההשכרה לא נמצאה");

        var callerIsRenter = loan.BorrowerId == userId;
        if (!callerIsRenter && item.OwnerId != userId)
            throw new KeyNotFoundException("ההשכרה לא נמצאה");
        if (loan.Status is not (LoanStatus.Active or LoanStatus.Overdue))
            throw new InvalidOperationException("אין כרגע השכרה פעילה");
        if (!IsPaid(loan))
            throw new InvalidOperationException("השיחה תתאפשר לאחר התשלום");

        var caller = await _users.GetByIdAsync(userId);
        var counterpart = await _users.GetByIdAsync(callerIsRenter ? item.OwnerId : loan.BorrowerId);
        if (caller is null || counterpart is null || string.IsNullOrWhiteSpace(caller.Phone) || string.IsNullOrWhiteSpace(counterpart.Phone))
            throw new InvalidOperationException("חסר מספר טלפון בחשבון");

        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        var recent = (await _logs.GetAllAsync()).Any(l =>
            l.Kind == "Bridge" && l.LoanId == loan.Id && l.ToUserId == counterpart.Id && l.CreatedAtUtc > cutoff);
        if (recent)
            throw new InvalidOperationException("שיחה כבר בדרך אלייך. אפשר לנסות שוב בעוד כמה דקות");

        var log = await _logs.CreateAsync(new CallLog
        {
            Kind = "Bridge",
            LoanId = loan.Id,
            ItemId = item.Id,
            ItemName = item.Name,
            ToUserId = counterpart.Id,
            ToName = counterpart.Name,
            ToPhone = counterpart.Phone,
            CreatedAtUtc = DateTime.UtcNow,
            PlacedAtUtc = DateTime.UtcNow,
            Status = "Placed",
            Detail = (callerIsRenter ? "שוכרת למשכירה" : "משכירה לשוכרת") + " (חיוג חוזר)"
        });

        var who = callerIsRenter ? "למשכירה" : "לשוכרת";
        var twiml = CallTwiml.Bridge(
            $"שלום. מחברים אותך {who}. נא להמתין.",
            ToE164(counterpart.Phone),
            _twilio.FromPhoneNumber,
            DoneUrl(log.Id));
        var result = await _calls.CallWithTwimlAsync(caller.Phone, twiml);
        if (!result.Placed)
        {
            log.Status = "Failed";
            log.Detail = $"{log.Detail} · {result.Error}";
            await _logs.UpdateAsync(log);
            throw new InvalidOperationException("לא הצלחנו לחייג כרגע. אפשר לנסות שוב בעוד כמה דקות");
        }

        log.TwilioSid = result.Sid;
        await _logs.UpdateAsync(log);
    }

    public async Task<string> HandleIncomingAsync(string fromPhone)
    {
        var blocked = CheckAllowed();
        if (blocked is not null)
            return CallTwiml.SayAndHangup(blocked);

        var (caller, candidates) = await FindCandidatesAsync(fromPhone);
        if (caller is null)
            return CallTwiml.SayAndHangup($"המספר שממנו חייגת אינו רשום באתר common. יש לחייג מהטלפון שהזנת בחשבון. {ContactHint}");

        // מי שלא ענתה לשיחה אוטומטית וחוזרת למספר - שומעת קודם את ההודעה האחרונה שנשלחה אליה.
        // בקשת הארכה / "האם הוחזר?" מושמעות עם תפריט עובד (התשובה מטופלת בדיוק כמו בשיחה המקורית)
        var (missed, menuUrl) = await TakeMissedMessageAsync(caller);
        if (missed is not null && menuUrl is not null)
            return CallTwiml.Gather(missed, menuUrl);
        string WithMissed(string twiml) => missed is null ? twiml : CallTwiml.Prepend(missed, twiml);

        var paid = candidates.Where(c => IsPaid(c.Loan)).ToList();
        if (missed is not null && paid.Count == 0)
            return CallTwiml.SayAndHangup($"{missed} תודה, להתראות.");

        if (candidates.Count == 0)
            return CallTwiml.SayAndHangup($"אין כרגע השכרה פעילה. {ContactHint}");

        if (paid.Count == 0)
            return CallTwiml.SayAndHangup($"השיחה תתאפשר לאחר התשלום. {ContactHint}");

        if (paid.Count == 1)
            return WithMissed(await ConnectAsync(paid[0]));

        var options = string.Join(" ", paid.Select((c, i) => $"לשיחה בעניין {c.Item.Name} הקישי {i + 1}."));
        return WithMissed(CallTwiml.Gather($"יש לך כמה השכרות פעילות. {options}", SelectUrl()));
    }

    private const string PlayedBackNote = "הושמעה כשחזרה למספר";
    private static readonly TimeSpan MissedMessageWindow = TimeSpan.FromDays(3);

    // ההודעה האחרונה שלא נענתה (3 ימים אחרונים), אם עוד לא הושמעה. מסומנת כהושמעה.
    // menuUrl - לבקשת הארכה ול"האם הוחזר?" שעדיין רלוונטיות: כתובת התפריט של השיחה המקורית
    private async Task<(string? Text, string? MenuUrl)> TakeMissedMessageAsync(User caller)
    {
        if (string.IsNullOrEmpty(caller.Id))
            return (null, null);
        try
        {
            var since = DateTime.UtcNow - MissedMessageWindow;
            var last = (await _logs.GetByUserIdAsync(caller.Id))
                .Where(l => l.Kind is not ("Bridge" or "Availability")
                            && !string.IsNullOrWhiteSpace(l.Message) && l.CreatedAtUtc >= since
                            && l.Status is "NoAnswer" or "Busy" or "Failed" or "Placed")
                .OrderByDescending(l => l.CreatedAtUtc)
                .FirstOrDefault();
            if (last is null || (last.Detail ?? string.Empty).Contains(PlayedBackNote))
                return (null, null);

            string? menuUrl = null;
            if (last.Kind is "ExtensionRequest" or "ReturnCheck")
            {
                menuUrl = await MenuUrlIfRelevantAsync(last);
                if (menuUrl is null)
                    return (null, null);
            }

            last.Detail = string.IsNullOrEmpty(last.Detail) ? PlayedBackNote : $"{last.Detail} · {PlayedBackNote}";
            await _logs.UpdateAsync(last);

            // בהשמעה חוזרת של תזכורת ההחזרה אין תפריט, ולכן מורידים את ההצעה להקיש 1 להארכה
            var message = menuUrl is null
                ? last.Message!.Replace("אם תרצי להאריך את ההשכרה ביום נוסף, הקישי 1. ", string.Empty)
                : last.Message!;
            return ($"שלום. זו ההודעה האחרונה שניסינו למסור לך מקומון. {message}", menuUrl);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<string?> MenuUrlIfRelevantAsync(CallLog log)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl) ? null : _twilio.PublicBaseUrl.TrimEnd('/');
        if (baseUrl is null || string.IsNullOrEmpty(log.LoanId) || string.IsNullOrEmpty(log.Id))
            return null;
        var loan = await _loans.GetByIdAsync(log.LoanId);
        if (loan is null || loan.Status != LoanStatus.Active)
            return null;

        if (log.Kind == "ExtensionRequest")
            return loan.ExtensionStatus == "Pending" && loan.ExpectedReturnDate > IsraelTime.Now
                ? $"{baseUrl}/api/loan-extensions/twilio/owner?loanId={loan.Id}&logId={log.Id}"
                : null;

        return loan.ExtensionStatus != "Pending"
            ? $"{baseUrl}/api/calls/twilio/return-check?loanId={loan.Id}&logId={log.Id}"
            : null;
    }

    public async Task<string> HandleSelectionAsync(string fromPhone, string digits)
    {
        var blocked = CheckAllowed();
        if (blocked is not null)
            return CallTwiml.SayAndHangup(blocked);

        var (_, all) = await FindCandidatesAsync(fromPhone);
        var candidates = all.Where(c => IsPaid(c.Loan)).ToList();
        if (!int.TryParse(digits, out var choice) || choice < 1 || choice > candidates.Count)
            return CallTwiml.SayAndHangup("הבחירה אינה תקינה. אפשר לחייג שוב.");

        return await ConnectAsync(candidates[choice - 1]);
    }

    public async Task<string> CompleteAsync(string logId, string dialStatus, int durationSeconds)
    {
        var log = string.IsNullOrWhiteSpace(logId) ? null : await _logs.GetByIdAsync(logId);
        if (log is not null)
        {
            log.Status = dialStatus switch
            {
                "completed" => "Answered",
                "no-answer" => "NoAnswer",
                "busy" => "Busy",
                _ => "Failed"
            };
            log.Detail = $"{log.Detail} · משך {durationSeconds} שניות";
            await _logs.UpdateAsync(log);
        }

        return dialStatus == "completed"
            ? CallTwiml.Empty()
            : CallTwiml.SayAndHangup($"השיחה לא נענתה. אפשר לנסות שוב מאוחר יותר. {ContactHint}");
    }

    private static string? CheckAllowed() =>
        CallWindow.IsAllowed(IsraelTime.Now)
            ? null
            : $"common סגורה כעת בשל שבת או חג. אפשר לנסות שוב במוצאי השבת או החג. {ContactHint}";

    private async Task<string> ConnectAsync(Candidate c)
    {
        if (QuietHours.IsQuiet(c.Counterpart, IsraelTime.Now))
            return CallTwiml.SayAndHangup($"הצלמת ביקשה לא לקבל שיחות בשעות הלילה. אפשר לנסות שוב בבוקר. {ContactHint}");
        var log = await _logs.CreateAsync(new CallLog
        {
            Kind = "Bridge",
            LoanId = c.Loan.Id,
            ItemId = c.Item.Id,
            ItemName = c.Item.Name,
            ToUserId = c.Counterpart.Id,
            ToName = c.Counterpart.Name,
            ToPhone = c.Counterpart.Phone,
            CreatedAtUtc = DateTime.UtcNow,
            PlacedAtUtc = DateTime.UtcNow,
            Status = "Placed",
            Detail = c.CallerIsRenter ? "שוכרת למשכירה" : "משכירה לשוכרת"
        });

        var who = c.CallerIsRenter ? "למשכירה" : "לשוכרת";
        return CallTwiml.Bridge(
            $"שיחתך מועברת {who}.",
            ToE164(c.Counterpart.Phone),
            string.IsNullOrWhiteSpace(_twilio.FromPhoneNumber) ? CommonNumber ?? string.Empty : _twilio.FromPhoneNumber,
            DoneUrl(log.Id));
    }

    private async Task<(User? Caller, List<Candidate> Candidates)> FindCandidatesAsync(string fromPhone)
    {
        var tail = Tail(fromPhone);
        var users = await _users.GetAllAsync();
        var caller = users.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u.Phone) && Tail(u.Phone) == tail);
        if (caller is null || string.IsNullOrEmpty(caller.Id))
            return (null, new List<Candidate>());

        var usersById = users.Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);
        var items = (await _items.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);

        var result = new List<Candidate>();
        foreach (var loan in await _loans.GetAllAsync())
        {
            if (loan.Status is not (LoanStatus.Active or LoanStatus.Overdue))
                continue;
            if (!items.TryGetValue(loan.ItemCopyId ?? string.Empty, out var item) || string.IsNullOrEmpty(item.OwnerId))
                continue;

            if (loan.BorrowerId == caller.Id && usersById.TryGetValue(item.OwnerId, out var owner))
                result.Add(new Candidate(loan, item, owner, CallerIsRenter: true));
            else if (item.OwnerId == caller.Id && !string.IsNullOrEmpty(loan.BorrowerId) && usersById.TryGetValue(loan.BorrowerId, out var renter))
                result.Add(new Candidate(loan, item, renter, CallerIsRenter: false));
        }

        var ordered = result
            .Where(c => !string.IsNullOrWhiteSpace(c.Counterpart.Phone))
            .OrderBy(c => c.Item.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Loan.Id, StringComparer.Ordinal)
            .Take(9)
            .ToList();
        return (caller, ordered);
    }

    private string SelectUrl() => $"{_twilio.PublicBaseUrl.TrimEnd('/')}/api/calls/twilio/inbound/select";

    private string DoneUrl(string? logId) => $"{_twilio.PublicBaseUrl.TrimEnd('/')}/api/calls/twilio/inbound/done?logId={Uri.EscapeDataString(logId ?? string.Empty)}";

    private static string Tail(string phone)
    {
        var at = phone.IndexOf('@');
        if (at >= 0) phone = phone[..at];
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return digits.Length <= 9 ? digits : digits[^9..];
    }

    private static string ToE164(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("972"))
            return "+" + digits;
        return digits.StartsWith('0') ? "+972" + digits[1..] : "+" + digits;
    }
}
