namespace PhotoRental.Service.Services;

using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// שיחות יוצאות (לשוכרת: תזכורת החזרה / "לא הוחזר" / עדכון הארכה; למשכירה: בקשת אישור הארכה) עם יומן שיחות (CallLog)
// ודחייה של שיחות שנופלות על שבת/חג למוצאי השבת/החג
public class OutboundCallService : IOutboundCallService
{
    private readonly ILoanRepository _loans;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly ICallLogRepository _logs;
    private readonly IReminderCallService _calls;
    private readonly TwilioOptions _twilio;

    public OutboundCallService(
        ILoanRepository loans,
        IItemRepository items,
        IUserRepository users,
        ICallLogRepository logs,
        IReminderCallService calls,
        TwilioOptions twilio,
        IEmailService email,
        IMarkerRepository markers,
        ClientOptions client)
    {
        _client = client;
        _email = email;
        _markers = markers;
        _loans = loans;
        _items = items;
        _users = users;
        _logs = logs;
        _calls = calls;
        _twilio = twilio;
    }

    private readonly IEmailService _email;
    private readonly ClientOptions _client;
    private readonly IMarkerRepository _markers;
    private const string InboxEmail = "common@onyxapp.co.il";
    private const int MaxReminderAttempts = 3;
    private const int MaxNoticeAttempts = 2;
    private static readonly TimeSpan ReminderRetryDelay = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PickupRetryDelay = TimeSpan.FromMinutes(15);

    public bool IsEnabled => _calls.IsEnabled;

    private static readonly TimeSpan WelcomeRetryDelay = TimeSpan.FromMinutes(13);

    public async Task WelcomeUnansweredAsync(CallLog log)
    {
        if (log.Kind != nameof(CallKind.Welcome) || log.Status is not ("NoAnswer" or "Busy" or "Failed")
            || string.IsNullOrEmpty(log.Id) || string.IsNullOrWhiteSpace(log.ToPhone))
            return;
        if (await _markers.ExistsAsync($"welcomeretry_{log.Id}"))
            return;
        await _markers.MarkAsync($"welcomeretry_{log.Id}");

        // ניסיון חוזר אחד בלבד: אם כבר היו שתי שיחות היכרות למספר הזה ביממה האחרונה - לא מתקשרים שוב
        var phoneKey = PhoneKey(log.ToPhone);
        var since = DateTime.UtcNow.AddHours(-24);
        var recent = (await _logs.GetAllAsync())
            .Count(l => l.Kind == nameof(CallKind.Welcome) && l.Status != "Canceled" && l.CreatedAtUtc >= since && PhoneKey(l.ToPhone) == phoneKey);
        if (recent >= 2)
            return;

        await _logs.CreateAsync(new CallLog
        {
            Kind = nameof(CallKind.Welcome),
            ToUserId = log.ToUserId,
            ToName = log.ToName,
            ToPhone = log.ToPhone,
            Message = log.Message,
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Scheduled",
            ScheduledForLocal = CallWindow.NextAllowed(IsraelTime.Now + WelcomeRetryDelay),
            Detail = "ניסיון חוזר - לא ענתה לשיחה הראשונה"
        });
    }

    public async Task CallEndedUnansweredAsync(CallLog log)
    {
        if (!Enum.TryParse<CallKind>(log.Kind, out var kind)
            || kind is not (CallKind.Reminder or CallKind.NotReturned or CallKind.ExtensionApproved or CallKind.ExtensionDeclined
                or CallKind.PickupReminder or CallKind.PickupOwnerReminder)
            || log.Status is not ("NoAnswer" or "Busy" or "Failed")
            || string.IsNullOrEmpty(log.LoanId) || string.IsNullOrEmpty(log.Id))
            return;
        if (await _markers.ExistsAsync($"reminderended_{log.Id}"))
            return;
        await _markers.MarkAsync($"reminderended_{log.Id}");

        var loan = await _loans.GetByIdAsync(log.LoanId);
        if (loan is null || loan.Status != LoanStatus.Active)
            return;

        var since = DateTime.UtcNow.AddHours(-6);
        var attempts = (await _logs.GetByLoanIdAsync(log.LoanId))
            .Count(l => l.Kind == log.Kind && l.Status != "Canceled" && l.CreatedAtUtc >= since);
        var max = kind == CallKind.Reminder ? MaxReminderAttempts : MaxNoticeAttempts;

        if (attempts < max)
        {
            var pickup = kind is CallKind.PickupReminder or CallKind.PickupOwnerReminder;
            if (pickup && loan.PickedUpAt is not null)
                return;
            await CallForLoanAsync(kind, loan, atLocal: IsraelTime.Now + (pickup ? PickupRetryDelay : ReminderRetryDelay),
                message: pickup ? log.Message : null);
            return;
        }

        if (kind != CallKind.Reminder)
            return;

        if (!_email.IsEnabled || await _markers.ExistsAsync($"remindernoanswer_{loan.Id}"))
            return;
        await _markers.MarkAsync($"remindernoanswer_{loan.Id}");

        try
        {
            var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
            var borrower = !string.IsNullOrEmpty(loan.BorrowerId) ? await _users.GetByIdAsync(loan.BorrowerId) : null;
            string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>השוכרת <strong>{H(borrower?.Name)}</strong> ({H(borrower?.Phone)}) לא ענתה ל-{MaxReminderAttempts} שיחות תזכורת להחזרת <strong>{H(item?.Name)}</strong>.</p>" +
                       $"<p>מועד ההחזרה: {loan.ExpectedReturnDate:dd/MM/yyyy HH:mm}. מזהה השכרה: {H(loan.Id)}</p>" +
                       "<p>כדאי ליצור איתה קשר.</p></div>";
            await _email.SendAsync(InboxEmail, "common", $"שוכרת לא ענתה לתזכורת החזרה - {item?.Name}", html);
        }
        catch
        {
            // מייל שלא נשלח לא עוצר את המשך הטיפול בשיחות
        }
    }

    // atLocal: לתזמן שיחה למועד מסוים (למשל ניסיון חוזר בעוד כמה דקות); אחרת - מיד, או במוצאי שבת/חג אם עכשיו אסור
    public async Task<CallOutcome> CallForLoanAsync(CallKind kind, Loan loan, Item? item = null, User? borrower = null, DateTime? atLocal = null, string? message = null)
    {
        if (!_calls.IsEnabled)
            return new CallOutcome();

        item ??= !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
        borrower ??= !string.IsNullOrEmpty(loan.BorrowerId) ? await _users.GetByIdAsync(loan.BorrowerId) : null;

        var recipient = await ResolveRecipientAsync(kind, item, borrower);
        if (recipient is null || string.IsNullOrWhiteSpace(recipient.Phone))
            return new CallOutcome();

        var nowLocal = IsraelTime.Now;
        var log = new CallLog
        {
            Kind = kind.ToString(),
            LoanId = loan.Id,
            ItemId = item?.Id,
            ItemName = item?.Name ?? string.Empty,
            ToUserId = recipient.Id,
            ToName = recipient.Name,
            ToPhone = recipient.Phone,
            CreatedAtUtc = DateTime.UtcNow,
            Message = message
        };

        var when = atLocal is not null && atLocal > nowLocal ? atLocal.Value : nowLocal;
        if (when > nowLocal || !QuietHours.IsAllowed(recipient, nowLocal))
        {
            log.Status = "Scheduled";
            log.ScheduledForLocal = QuietHours.NextAllowed(recipient, when);
            await _logs.CreateAsync(log);
            return new CallOutcome { ScheduledForLocal = log.ScheduledForLocal };
        }

        await PlaceAsync(log, kind, loan, item, borrower, recipient, nowLocal, message);
        return new CallOutcome { Placed = log.Status == "Placed" };
    }

    public async Task SendMissedCallEmailAsync(CallLog log)
    {
        if (!_email.IsEnabled || string.IsNullOrEmpty(log.LoanId) || string.IsNullOrEmpty(log.ToUserId)
            || !Enum.TryParse<CallKind>(log.Kind, out var kind)
            || kind is not (CallKind.ExtensionRequest or CallKind.PickupReminder or CallKind.PickupOwnerReminder))
            return;

        var key = $"missedmail_{log.LoanId}_{log.Kind}";
        if (await _markers.ExistsAsync(key))
            return;
        await _markers.MarkAsync(key);

        var loan = await _loans.GetByIdAsync(log.LoanId);
        var to = await _users.GetByIdAsync(log.ToUserId);
        if (loan is null || to is null || string.IsNullOrWhiteSpace(to.Email))
            return;
        if (kind == CallKind.ExtensionRequest && loan.ExtensionStatus != "Pending")
            return;
        if (kind is CallKind.PickupReminder or CallKind.PickupOwnerReminder && loan.PickedUpAt is not null)
            return;

        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
        string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        const string button = "display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none";

        string subject, body;
        if (kind == CallKind.ExtensionRequest)
        {
            var days = Math.Max(1, loan.ExtensionDays);
            var newReturn = loan.ExpectedReturnDate.AddDays(days);
            var link = $"{baseUrl}/loan-action/{loan.Id}?t={loan.OwnerActionToken}";
            subject = $"בקשת הארכה - {item?.Name} - common";
            body = $"<p>ניסינו להתקשר אלייך ולא הצלחנו.</p>" +
                   $"<p>השוכרת של <strong>{H(item?.Name)}</strong> מבקשת להאריך את ההשכרה ב-{(days == 1 ? "יום אחד" : $"{days} ימים")}, עד {newReturn:dd/MM/yyyy HH:mm}.</p>" +
                   $"<p><a href=\"{H(link)}\" style=\"{button}\"><strong>לאישור או דחיית ההארכה</strong></a></p>";
        }
        else
        {
            subject = $"תזכורת: {item?.Name} - common";
            body = $"<p>ניסינו להתקשר אלייך כדי להזכיר:</p><p>{H(log.Message)}</p>" +
                   (kind == CallKind.PickupReminder
                       ? $"<p><a href=\"{H(baseUrl)}/order/{H(loan.Id)}\" style=\"{button}\"><strong>פרטי ההזמנה</strong></a></p>"
                       : string.Empty);
        }

        var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                   $"<p>שלום {H(NameText.FirstName(to.Name))},</p>" + body + "<p>common</p></div>";
        try { await _email.SendAsync(to.Email, to.Name, subject, html); } catch { }
    }

    public async Task<CallOutcome> CallWelcomeAsync(User user)
    {
        if (!_calls.IsEnabled || string.IsNullOrEmpty(user.Id) || string.IsNullOrWhiteSpace(user.Phone))
            return new CallOutcome();

        var previous = await _logs.GetByUserIdAsync(user.Id);
        var phoneKey = PhoneKey(user.Phone);
        if (previous.Any(l => l.Kind == nameof(CallKind.Welcome) && l.Status is not ("Canceled" or "Failed") && PhoneKey(l.ToPhone) == phoneKey))
            return new CallOutcome();

        var nowLocal = IsraelTime.Now;
        var log = new CallLog
        {
            Kind = nameof(CallKind.Welcome),
            ToUserId = user.Id,
            ToName = user.Name,
            ToPhone = user.Phone,
            CreatedAtUtc = DateTime.UtcNow
        };

        var when = CallWindow.NextAllowed(nowLocal);
        if (when > nowLocal)
        {
            log.Status = "Scheduled";
            log.ScheduledForLocal = when;
            await _logs.CreateAsync(log);
            return new CallOutcome { ScheduledForLocal = when };
        }

        await PlaceWelcomeAsync(log, user);
        return new CallOutcome { Placed = log.Status == "Placed" };
    }

    public async Task<CallOutcome> CallMessageAsync(User user, CallKind kind, string message, string? itemName = null)
    {
        if (!_calls.IsEnabled || string.IsNullOrEmpty(user.Id) || string.IsNullOrWhiteSpace(user.Phone))
            return new CallOutcome();

        var nowLocal = IsraelTime.Now;
        var log = new CallLog
        {
            Kind = kind.ToString(),
            ToUserId = user.Id,
            ToName = user.Name,
            ToPhone = user.Phone,
            ItemName = itemName ?? string.Empty,
            Message = message,
            CreatedAtUtc = DateTime.UtcNow
        };

        var when = QuietHours.NextAllowed(user, nowLocal);
        if (when > nowLocal)
        {
            log.Status = "Scheduled";
            log.ScheduledForLocal = when;
            await _logs.CreateAsync(log);
            return new CallOutcome { ScheduledForLocal = when };
        }

        await PlaceMessageAsync(log, user);
        return new CallOutcome { Placed = log.Status == "Placed" };
    }

    private async Task PlaceMessageAsync(CallLog log, User user)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl) ? null : _twilio.PublicBaseUrl.TrimEnd('/');
        log.ToPhone = user.Phone;
        if (string.IsNullOrEmpty(log.Id))
        {
            log.Status = "Placed";
            await _logs.CreateAsync(log);
        }
        var statusCallback = baseUrl is null || string.IsNullOrEmpty(log.Id) ? null : $"{baseUrl}/api/calls/twilio/status?logId={log.Id}";
        var result = await _calls.CallAsync(user.Phone, log.Message ?? string.Empty, repeat: true, statusCallbackUrl: statusCallback);
        log.Status = result.Placed ? "Placed" : "Failed";
        log.Detail = result.Placed ? null : result.Error;
        log.TwilioSid = result.Sid;
        log.PlacedAtUtc = DateTime.UtcNow;
        await _logs.UpdateAsync(log);
    }

    // 050-1234567 / +972501234567 / 0501234567 - אותו מספר
    private static string PhoneKey(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length > 9 ? digits[^9..] : digits;
    }

    private async Task PlaceWelcomeAsync(CallLog log, User user)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl) ? null : _twilio.PublicBaseUrl.TrimEnd('/');
        log.ToPhone = user.Phone;
        log.Message = CallMessages.Welcome(user, CallMessages.SpokenPhone(_twilio.FromPhoneNumber));
        if (string.IsNullOrEmpty(log.Id))
        {
            log.Status = "Placed";
            await _logs.CreateAsync(log);
        }

        string? statusCallback = baseUrl is null || string.IsNullOrEmpty(log.Id)
            ? null
            : $"{baseUrl}/api/calls/twilio/status?logId={log.Id}";

        var result = await _calls.CallAsync(user.Phone, log.Message, repeat: false, statusCallbackUrl: statusCallback);

        log.Status = result.Placed ? "Placed" : "Failed";
        log.Detail = result.Placed ? null : result.Error;
        log.TwilioSid = result.Sid;
        log.PlacedAtUtc = DateTime.UtcNow;
        await _logs.UpdateAsync(log);
    }

    public async Task ProcessScheduledAsync()
    {
        if (!_calls.IsEnabled)
            return;

        var nowLocal = IsraelTime.Now;
        var due = (await _logs.GetByStatusAsync("Scheduled"))
            .Where(l => l.Status == "Scheduled" && l.ScheduledForLocal is not null && l.ScheduledForLocal <= nowLocal)
            .ToList();
        if (due.Count == 0 || !CallWindow.IsAllowed(nowLocal))
            return;

        foreach (var log in due)
        {
            var kind = Enum.TryParse<CallKind>(log.Kind, out var parsed) ? parsed : CallKind.Reminder;

            var target = kind == CallKind.Welcome || string.IsNullOrEmpty(log.ToUserId) ? null : await _users.GetByIdAsync(log.ToUserId);
            if (QuietHours.IsQuiet(target, nowLocal))
            {
                log.ScheduledForLocal = QuietHours.NextAllowed(target, nowLocal);
                await _logs.UpdateAsync(log);
                continue;
            }

            if (kind == CallKind.Welcome)
            {
                var member = !string.IsNullOrEmpty(log.ToUserId) ? await _users.GetByIdAsync(log.ToUserId) : null;
                if (member is null || string.IsNullOrWhiteSpace(member.Phone))
                {
                    log.Status = "Canceled";
                    log.Detail = "החשבון נמחק או שאין בו מספר טלפון";
                    await _logs.UpdateAsync(log);
                    continue;
                }
                if (PhoneKey(member.Phone) != PhoneKey(log.ToPhone))
                {
                    log.Status = "Canceled";
                    log.Detail = "המספר הוחלף - נקבעה שיחה למספר החדש";
                    await _logs.UpdateAsync(log);
                    continue;
                }
                await PlaceWelcomeAsync(log, member);
                continue;
            }
            if (kind == CallKind.SaleNotice)
            {
                if (target is null || string.IsNullOrWhiteSpace(target.Phone))
                {
                    log.Status = "Canceled";
                    log.Detail = "החשבון נמחק או שאין בו מספר טלפון";
                    await _logs.UpdateAsync(log);
                    continue;
                }
                await PlaceMessageAsync(log, target);
                continue;
            }
            var loan = !string.IsNullOrEmpty(log.LoanId) ? await _loans.GetByIdAsync(log.LoanId) : null;

            // שיחות תזכורת / "לא הוחזר" / בקשת הארכה כבר לא רלוונטיות אם הציוד הוחזר או שהבקשה טופלה
            var needsActiveLoan = kind is CallKind.Reminder or CallKind.NotReturned or CallKind.ExtensionRequest or CallKind.ReturnCheck
                or CallKind.PickupReminder or CallKind.PickupOwnerReminder;
            var stale = loan is null
                || (needsActiveLoan && loan.Status != LoanStatus.Active)
                || (kind == CallKind.ExtensionRequest && loan.ExtensionStatus != "Pending")
                || (kind == CallKind.ReturnCheck && loan.ExtensionStatus == "Pending")
                || (kind is CallKind.PickupReminder or CallKind.PickupOwnerReminder && loan.PickedUpAt is not null);
            if (stale)
            {
                log.Status = "Canceled";
                log.Detail = "השיחה כבר לא רלוונטית (הציוד הוחזר או שהבקשה טופלה)";
                await _logs.UpdateAsync(log);
                continue;
            }

            var item = !string.IsNullOrEmpty(loan!.ItemCopyId) ? await _items.GetByIdAsync(loan.ItemCopyId) : null;
            var borrower = !string.IsNullOrEmpty(loan.BorrowerId) ? await _users.GetByIdAsync(loan.BorrowerId) : null;
            var recipient = await ResolveRecipientAsync(kind, item, borrower);
            if (item is null || borrower is null || recipient is null || string.IsNullOrWhiteSpace(recipient.Phone))
            {
                log.Status = "Failed";
                log.Detail = "חסרים פרטי נמענת או פריט";
                await _logs.UpdateAsync(log);
                continue;
            }

            await PlaceAsync(log, kind, loan, item, borrower, recipient, nowLocal, kind is CallKind.PickupReminder or CallKind.PickupOwnerReminder ? log.Message : null);
        }
    }

    // בקשת אישור הארכה מגיעה למשכירה; כל שאר השיחות - לשוכרת
    private async Task<User?> ResolveRecipientAsync(CallKind kind, Item? item, User? borrower)
    {
        if (kind is CallKind.ExtensionRequest or CallKind.ReturnCheck or CallKind.PickupOwnerReminder)
            return !string.IsNullOrEmpty(item?.OwnerId) ? await _users.GetByIdAsync(item.OwnerId) : null;
        return borrower;
    }

    private async Task PlaceAsync(CallLog log, CallKind kind, Loan loan, Item? item, User? borrower, User recipient, DateTime nowLocal, string? messageOverride = null)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_twilio.PublicBaseUrl) ? null : _twilio.PublicBaseUrl.TrimEnd('/');
        var canGather = baseUrl is not null;
        var safeItem = item ?? new Item { Name = "הציוד" };
        var safeBorrower = borrower ?? new User { Name = "השוכרת" };

        var message = !string.IsNullOrWhiteSpace(messageOverride) ? messageOverride : kind switch
        {
            CallKind.NotReturned => CallMessages.NotReturned(safeBorrower, safeItem.Name, loan.PaymentMethod == PaymentMethod.CreditCard),
            CallKind.ExtensionRequest => CallMessages.ExtensionRequest(safeItem, loan, Math.Max(1, loan.ExtensionDays)),
            CallKind.ExtensionApproved => CallMessages.ExtensionApproved(safeBorrower, safeItem, loan),
            CallKind.ExtensionDeclined => CallMessages.ExtensionDeclined(safeBorrower, safeItem),
            CallKind.ExtensionChargeFailed => CallMessages.ExtensionChargeFailed(safeBorrower, safeItem),
            CallKind.ReturnCheck => CallMessages.ReturnCheck(recipient, safeItem.Name),
            // תזכורת החזרה: כשאפשר - מציעה גם להאריך ביום נוסף בהקשת 1
            _ => CallMessages.Reminder(safeBorrower, safeItem, loan, nowLocal, offerExtension: canGather && loan.ExpectedReturnDate > nowLocal)
        };

        log.Message = message;
        if (string.IsNullOrEmpty(log.Id))
        {
            log.Status = "Placed";
            await _logs.CreateAsync(log);
        }

        string? statusCallback = baseUrl is null || string.IsNullOrEmpty(log.Id)
            ? null
            : $"{baseUrl}/api/calls/twilio/status?logId={log.Id}";

        // תפריט (הקשת ספרה): בתזכורת - השוכרת מבקשת הארכה; בבקשת הארכה - המשכירה מאשרת/דוחה
        string? gatherUrl = null;
        if (canGather && !string.IsNullOrEmpty(log.Id) && !string.IsNullOrEmpty(loan.Id))
        {
            if (kind == CallKind.Reminder && loan.ExpectedReturnDate > nowLocal)
                gatherUrl = $"{baseUrl}/api/loan-extensions/twilio/renter?loanId={loan.Id}&logId={log.Id}";
            else if (kind == CallKind.ExtensionRequest)
                gatherUrl = $"{baseUrl}/api/loan-extensions/twilio/owner?loanId={loan.Id}&logId={log.Id}";
            else if (kind == CallKind.ReturnCheck)
                gatherUrl = $"{baseUrl}/api/calls/twilio/return-check?loanId={loan.Id}&logId={log.Id}";
        }

        // ההודעה נאמרת פעם אחת בלבד
        var result = await _calls.CallAsync(recipient.Phone, message, repeat: false, statusCallbackUrl: statusCallback, gatherActionUrl: gatherUrl);

        log.Status = result.Placed ? "Placed" : "Failed";
        log.Detail = result.Placed ? null : result.Error;
        log.TwilioSid = result.Sid;
        log.PlacedAtUtc = DateTime.UtcNow;
        await _logs.UpdateAsync(log);
    }
}
