namespace PhotoRental.API.BackgroundServices;

using System.Net;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;
using PhotoRental.Service.Services;

// תזכורות החזרה - שירות רקע שרץ כל כמה דקות לאורך חיי השרת (Singleton דרך AddHostedService, ולכן פותח Scope
// חדש בכל סבב עבור ה-Repositories/Services שהם Scoped):
//  1. שעתיים לפני סיום ההשכרה: מייל + שיחת טלפון לשוכרת ("היי זמינה להחזיר את הציוד").
//  2. חצי שעה אחרי מועד ההחזרה: מייל למשכירה "האם X הוחזר?" (ההודעה האוטומטית "הציוד טרם הוחזר" בוטלה -
//     הודעה לשוכרת על איחור נשלחת רק אחרי שהמשכירה מסמנת שהפריט עדיין לא הוחזר).
// מועדי ההחזרה נשמרים כשעון קיר מקומי של ישראל ולכן ההשוואה היא מול שעון ישראל (ראו IsraelTime).
public class ReturnReminderBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReminderWindow = TimeSpan.FromHours(2);

    // סבבים לא חופפים (הלולאה הפנימית וקריאה חיצונית מ-Cloud Scheduler) - כדי שלא תישלח אותה תזכורת פעמיים
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTime _lastDebtNoticesUtc = DateTime.MinValue;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SchedulerOptions _scheduler;

    public ReturnReminderBackgroundService(IServiceScopeFactory scopeFactory, SchedulerOptions scheduler)
    {
        _scopeFactory = scopeFactory;
        _scheduler = scheduler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_scheduler.External) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunRoundAsync();
            }
            catch
            {
                // כשל בסבב בודד (למשל בעיית רשת רגעית מול Firestore) לא מפיל את השירות - מנסים שוב בסבב הבא
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // השרת נסגר - יציאה שקטה מהלולאה
            }
        }
    }

    // סבב בדיקה אחד. נקרא מהלולאה הפנימית, וגם מ-POST /api/internal/scheduled-runs (ראו SchedulerController)
    public async Task RunRoundAsync()
    {
        await Gate.WaitAsync();
        try
        {
            await RunRoundCoreAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task RunRoundCoreAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var loanRepository = scope.ServiceProvider.GetRequiredService<ILoanRepository>();
        var itemRepository = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var callService = scope.ServiceProvider.GetRequiredService<IOutboundCallService>();
        var clientOptions = scope.ServiceProvider.GetRequiredService<ClientOptions>();
        var logRepository = scope.ServiceProvider.GetRequiredService<ICallLogRepository>();

        // שיחות שנדחו (שבת/חג) וזמנן הגיע - מחייגים עכשיו
        try { await callService.ProcessScheduledAsync(); } catch { /* שיחה שנכשלה לא עוצרת את שאר הסבב */ }

        // פריטים שהתפנו - מייל למי שביקשה "תודיעו לי"; תזכורות לפני קבלת הציוד
        try { await scope.ServiceProvider.GetRequiredService<IFavoriteService>().NotifyAvailableAsync(); } catch { }
        try { await scope.ServiceProvider.GetRequiredService<IPickupReminderService>().ProcessAsync(); } catch { }
        try { await scope.ServiceProvider.GetRequiredService<IAvailabilityCallService>().ProcessRetriesAsync(); } catch { }
        try { await scope.ServiceProvider.GetRequiredService<ILoanService>().DispatchPendingExpressAsync(); } catch { }
        try { await scope.ServiceProvider.GetRequiredService<IItemScheduleService>().RefreshAllStatusesAsync(); } catch { }

        // בקשות הארכה שלא אושרו עד מועד ההחזרה - נסגרות כדחויות והשוכרת מקבלת הודעה
        try { await scope.ServiceProvider.GetRequiredService<ILoanExtensionService>().ExpirePendingAsync(); } catch { }

        // תזכורת (7 ימים) והודעת חסימה (14 ימים) למשכירות עם דוח עמלה שלא שולם
        if (DateTime.UtcNow - _lastDebtNoticesUtc >= TimeSpan.FromHours(1))
        {
            _lastDebtNoticesUtc = DateTime.UtcNow;
            try { await scope.ServiceProvider.GetRequiredService<ICommissionDebtService>().ProcessNoticesAsync(); } catch { }
            try { await scope.ServiceProvider.GetRequiredService<IReviewService>().SendReviewRequestsAsync(); } catch { }
        }

        var nowLocal = IsraelTime.Now;
        var activeLoans = (await loanRepository.GetByStatusAsync(LoanStatus.Active)).ToList();

        foreach (var loan in activeLoans)
        {
            if (string.IsNullOrEmpty(loan.ItemCopyId) || string.IsNullOrEmpty(loan.BorrowerId))
                continue;

            // רבע שעה אחרי תחילת ההשכרה: מייל למשכירה "הפריט נאסף?" עם קישור לחלון הסימון
            // (רק להשכרות שנוצרו אחרי הוספת התכונה - יש להן טוקן סימון)
            if (!loan.IsLocation && loan.PickupPromptSentAt is null && loan.PickedUpAt is null && !string.IsNullOrEmpty(loan.OwnerActionToken))
            {
                var startLocal = loan.StartDate.Date + ParseStartTime(loan.StartTime);
                if (nowLocal >= startLocal.AddMinutes(15))
                {
                    try { await SendRenterPickupPromptAsync(loan, itemRepository, userRepository, emailService, clientOptions); } catch { }
                    await SendPickupPromptAsync(loan, itemRepository, userRepository, emailService, loanRepository, clientOptions);
                    if (emailService.IsEnabled)
                        await loanRepository.UpdatePickupPromptSentAsync(loan.Id!, DateTime.UtcNow);
                }
            }

            // חצי שעה אחרי מועד ההחזרה: מייל למשכירה "האם X הוחזר?" עם שני קישורים (עד 3 ימים אחרי, כדי לא להציף
            // השכרות ישנות שחלפו מזמן)
            if (loan.ReturnPromptSentAt is null && !string.IsNullOrEmpty(loan.OwnerActionToken) &&
                nowLocal >= loan.ExpectedReturnDate.AddMinutes(30) && nowLocal <= loan.ExpectedReturnDate.AddDays(3))
                await SendReturnPromptAsync(loan, itemRepository, userRepository, emailService, loanRepository, clientOptions);

            // 45 דקות אחרי מועד ההחזרה: שיחה למשכירה "האם הפריט הוחזר?" - רק אם לא סימנה במייל כלום או סימנה "לא הוחזר"
            // (סימון "הוחזר" סוגר את ההשכרה ולכן אין שיחה), ולא כשממתינה בקשת הארכה.
            // אחרי הארכה שאושרה מועד ההחזרה זז, ולכן השיחה (כמו שאר התזכורות) חוזרת במועד החדש
            if (!loan.IsLocation && loan.ExtensionStatus != "Pending" &&
                nowLocal >= loan.ExpectedReturnDate.AddMinutes(45) && nowLocal <= loan.ExpectedReturnDate.AddDays(3))
            {
                var loanLogs = await logRepository.GetByLoanIdAsync(loan.Id!);
                var alreadyChecked = loanLogs.Any(l =>
                    l.Kind == nameof(CallKind.ReturnCheck) && l.LoanId == loan.Id && IsraelTime.FromUtc(l.CreatedAtUtc) >= loan.ExpectedReturnDate);
                if (!alreadyChecked)
                {
                    try { await callService.CallForLoanAsync(CallKind.ReturnCheck, loan); } catch { }
                }
            }

            var untilReturn = loan.ExpectedReturnDate - nowLocal;
            var needsReminder = !loan.IsLocation && loan.ReturnReminderSentAt is null && untilReturn > TimeSpan.Zero && untilReturn <= ReminderWindow;
            if (!needsReminder)
                continue;

            var item = await itemRepository.GetByIdAsync(loan.ItemCopyId);
            var borrower = await userRepository.GetByIdAsync(loan.BorrowerId);
            if (item is null || borrower is null)
                continue;

            await SendReminderAsync(loan, item, borrower, emailService, callService, loanRepository, clientOptions);
        }
    }

    // שעת תחילת ההשכרה ("HH:mm"); כשלא נבחרה - 12:00 (כמו ברירת המחדל בחלון ההשכרה)
    private static TimeSpan ParseStartTime(string? startTime) =>
        !string.IsNullOrWhiteSpace(startTime) && TimeSpan.TryParseExact(startTime, "hh\\:mm", System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : new TimeSpan(12, 0, 0);

    private static async Task SendRenterPickupPromptAsync(
        Loan loan, IItemRepository itemRepository, IUserRepository userRepository, IEmailService emailService, ClientOptions clientOptions)
    {
        var token = PhotoRental.Service.Services.LoanService.RenterActionToken(loan);
        if (!emailService.IsEnabled || string.IsNullOrEmpty(loan.ItemCopyId) || string.IsNullOrEmpty(loan.BorrowerId) || token is null)
            return;

        var item = await itemRepository.GetByIdAsync(loan.ItemCopyId);
        var renter = await userRepository.GetByIdAsync(loan.BorrowerId);
        if (item is null || renter is null || string.IsNullOrWhiteSpace(renter.Email))
            return;

        var h = (string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var baseUrl = clientOptions.BaseUrl.TrimEnd('/');
        var link = $"{baseUrl}/pickup/{loan.Id}?t={token}";
        var delivery = loan.DeliveryMethod == DeliveryMethod.Delivery;
        var button = delivery ? "הפריט הגיע אליי" : "אספתי את הפריט";

        var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                   $"<p>שלום {h(NameText.FirstName(renter.Name))},</p>" +
                   $"<p>קיבלת את <strong>{h(LoanText.Name(item, loan))}</strong>?</p>" +
                   $"<p><a href=\"{h(link)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>{button}</strong></a></p>" +
                   $"<p><a href=\"{h(baseUrl)}/order/{h(loan.Id)}\">פרטי ההזמנה</a></p>" +
                   "<p>common</p></div>";

        await emailService.SendAsync(renter.Email, renter.Name, $"קיבלת את {item.Name}? - common", html);
    }

    private static async Task SendPickupPromptAsync(
        Loan loan, IItemRepository itemRepository, IUserRepository userRepository, IEmailService emailService,
        ILoanRepository loanRepository, ClientOptions clientOptions)
    {
        if (!emailService.IsEnabled || string.IsNullOrEmpty(loan.ItemCopyId))
            return;

        var item = await itemRepository.GetByIdAsync(loan.ItemCopyId);
        var owner = !string.IsNullOrEmpty(item?.OwnerId) ? await userRepository.GetByIdAsync(item.OwnerId) : null;
        if (item is null || owner is null || string.IsNullOrWhiteSpace(owner.Email))
            return;

        var h = (string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var link = $"{clientOptions.BaseUrl.TrimEnd('/')}/loan-action/{loan.Id}?t={loan.OwnerActionToken}";
        var amount = (loan.RentalDays * loan.RateOr(item.DailyPrice) * loan.Units).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        var paymentLine = loan.PaymentMethod switch
        {
            PaymentMethod.Cash when loan.PaymentStatus != PaymentStatus.Paid =>
                $"<p style=\"background:#ffe2e2;border:2px solid #c00;border-radius:10px;padding:10px\"><strong>התשלום במזומן: ₪{amount}.</strong> אם שולם לך במזומן - חשוב לסמן בחלון שייפתח מהכפתור שהתשלום התקבל.</p>",
            PaymentMethod.BankTransfer =>
                "<p><strong>התשלום בוצע בהעברה בנקאית</strong> - האסמכתא זמינה באזור האישי.</p>",
            _ => string.Empty
        };

        var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                   $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                   $"<p>ההשכרה של <strong>{h(LoanText.Name(item, loan))}</strong> אמורה הייתה להתחיל. האם הפריט נאסף ממך?</p>" +
                   "<p>אם הפריט כבר נאסף ממך (על ידי השוכרת או השליח), לחצי על הכפתור הירוק ובחלון שייפתח סמני \"הפריט נאסף\".</p>" +
                   paymentLine +
                   $"<p><a href=\"{h(link)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>סימון שהפריט נאסף ממך / שהתשלום התקבל</strong></a></p>" +
                   "</div>";

        await emailService.SendAsync(owner.Email, owner.Name, "הפריט נאסף? - common", html);
        await loanRepository.UpdatePickupPromptSentAsync(loan.Id!, DateTime.UtcNow);
    }

    private static async Task SendReturnPromptAsync(
        Loan loan, IItemRepository itemRepository, IUserRepository userRepository, IEmailService emailService,
        ILoanRepository loanRepository, ClientOptions clientOptions)
    {
        if (!emailService.IsEnabled || string.IsNullOrEmpty(loan.ItemCopyId))
            return;

        var item = await itemRepository.GetByIdAsync(loan.ItemCopyId);
        var owner = !string.IsNullOrEmpty(item?.OwnerId) ? await userRepository.GetByIdAsync(item.OwnerId) : null;
        if (item is null || owner is null || string.IsNullOrWhiteSpace(owner.Email))
            return;

        var h = (string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        var baseLink = $"{clientOptions.BaseUrl.TrimEnd('/')}/loan-action/{loan.Id}?t={loan.OwnerActionToken}";
        var returnedLink = $"{baseLink}&a=returned";
        var notReturnedLink = $"{baseLink}&a=not-returned";
        var due = loan.ExpectedReturnDate.ToString("dd/MM/yyyy HH:mm");

        if (loan.IsLocation)
        {
            var locationHtml = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                       $"<p>הצילום ב<strong>{h(item.Name)}</strong> הסתיים ב-{due}.</p>" +
                       "<p><strong>הכול עבר בסדר והמקום פונה?</strong></p>" +
                       $"<p><a href=\"{h(returnedLink)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>כן, הצילום הסתיים</strong></a></p>" +
                       $"<p><a href=\"{h(notReturnedLink)}\" style=\"display:inline-block;background:#e89c9c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>עדיין לא / יש בעיה</strong></a></p>" +
                       "</div>";
            await emailService.SendAsync(owner.Email, owner.Name, $"הצילום ב{item.Name} הסתיים? - common", locationHtml);
            await loanRepository.UpdateReturnPromptSentAsync(loan.Id!, DateTime.UtcNow);
            return;
        }

        var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                   $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                   $"<p>ההשכרה של <strong>{h(LoanText.Name(item, loan))}</strong> הייתה אמורה להסתיים ב-{due}.</p>" +
                   $"<p><strong>האם {h(LoanText.Name(item, loan))} הוחזר?</strong></p>" +
                   $"<p><a href=\"{h(returnedLink)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>לחצי כאן לסימון ש-{h(LoanText.Name(item, loan))} הוחזר</strong></a></p>" +
                   $"<p><a href=\"{h(notReturnedLink)}\" style=\"display:inline-block;background:#e89c9c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>לחצי כאן לסימון ש-{h(LoanText.Name(item, loan))} עדיין לא הוחזר</strong></a></p>" +
                   "<p style=\"color:#666;font-size:13px\">אם סימנת שעדיין לא הוחזר - נתקשר ונשלח מייל לשוכרת, ונטפל בכך. איחור מחויב ביום השכרה נוסף על כל יום.</p>" +
                   "</div>";

        await emailService.SendAsync(owner.Email, owner.Name, $"האם {item.Name} הוחזר? - common", html);
        await loanRepository.UpdateReturnPromptSentAsync(loan.Id!, DateTime.UtcNow);
    }

    private static async Task SendReminderAsync(
        Loan loan, Item item, User borrower, IEmailService emailService, IOutboundCallService callService, ILoanRepository loanRepository,
        ClientOptions clientOptions)
    {
        var canEmail = emailService.IsEnabled && !string.IsNullOrWhiteSpace(borrower.Email);
        var canCall = callService.IsEnabled && !string.IsNullOrWhiteSpace(borrower.Phone);
        if (!canEmail && !canCall)
            return; // אין ערוץ זמין כרגע - לא מסמנים כנשלח, כדי לנסות שוב כשיוגדר

        var returnTime = loan.ExpectedReturnDate.ToString("HH:mm");
        var returnDate = loan.ExpectedReturnDate.ToString("dd/MM/yyyy");
        var nowLocal = IsraelTime.Now;
        var remainingText = CallMessages.DescribeRemaining(loan.ExpectedReturnDate - nowLocal);
        var dayDelta = (loan.ExpectedReturnDate.Date - nowLocal.Date).Days;
        var dayLabel = dayDelta switch { 0 => "היום", 1 => "מחר", _ => returnDate };
        var returnMethodLabel = loan.ReturnMethod == ReturnMethod.CourierPickup
            ? "איסוף שליח מהכתובת שסיפקת"
            : "מסירה עצמית למשכיר";

        if (canEmail)
        {
            try
            {
                var html = $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {WebUtility.HtmlEncode(NameText.FirstName(borrower.Name))},</p>" +
                           $"<p>תזכורת: ההשכרה של <strong>{WebUtility.HtmlEncode(LoanText.Name(item, loan))}</strong> מסתיימת בעוד {remainingText} - {dayLabel} ב-{returnTime} ({returnDate}). אנא היי זמינה להחזיר את הציוד בזמן.</p>" +
                           $"<p>אופן ההחזרה שנקבע: {returnMethodLabel}.</p>" +
                           PhotoRental.Service.Services.LoanService.ReturnChangeLinkHtml(clientOptions.BaseUrl, loan) +
                           $"<p><strong>שימי לב:</strong> איחור בהחזרה מחויב ביום השכרה נוסף (₪{loan.RateOr(item.DailyPrice) * loan.Units:0.##}) על כל יום איחור.</p>" +
                           "<p>תודה שאת חלק מקהילת common!</p></div>";
                await emailService.SendAsync(borrower.Email, borrower.Name, $"תזכורת: החזרת הציוד בעוד {remainingText} - common", html);
            }
            catch
            {
                // כשל במייל לא מונע את השיחה
            }
        }

        if (canCall)
        {
            // שיחה אחת, שעתיים לפני ההחזרה. אם זה שבת/חג - היא נדחית אוטומטית למוצאי השבת/החג (ראו CallWindow)
            await callService.CallForLoanAsync(CallKind.Reminder, loan, item, borrower);
        }

        await loanRepository.UpdateReturnReminderSentAsync(loan.Id!, DateTime.UtcNow);
    }
}
