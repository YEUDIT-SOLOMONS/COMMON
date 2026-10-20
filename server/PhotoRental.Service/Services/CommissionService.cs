namespace PhotoRental.Service.Services;

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// עמלת השירות של האתר: 8% (ברירת מחדל) מסכום הציוד של כל השכרה ש**הוחזרה**, נגבית מהמשכירה פעם בחודש.
// ההשכרה נספרת בחודש שבו הוחזרה בפועל (לפי שעון ישראל), ללא דמי משלוח. הסכומים מחושבים תמיד מחדש
// מההשכרות עצמן (אין "מקור אמת" שני), ובטבלת CommissionStatements נשמר רק מצב הגבייה: נשלח / שולם.
public class CommissionService : ICommissionService
{
    // תיבת הדואר של האתר - לשם נשלחות אסמכתאות שהועלו דרך הקישור בדוח
    private const string InboxEmail = "common@onyxapp.co.il";

    private static readonly Regex MonthPattern = new(@"^\d{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private static readonly string[] HebrewMonths =
    {
        "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני", "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר"
    };

    private readonly ILoanRepository _loanRepository;
    private readonly IItemRepository _itemRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICommissionStatementRepository _statementRepository;
    private readonly IEmailService _emailService;
    private readonly CommissionOptions _options;
    private readonly ICardPaymentService _cardPayments;
    private readonly IPaymentProofRepository _paymentProofRepository;
    private readonly ClientOptions _clientOptions;

    private enum AutoChargeOutcome { None, Charged, Failed }

    public CommissionService(
        ILoanRepository loanRepository,
        IItemRepository itemRepository,
        IUserRepository userRepository,
        ICommissionStatementRepository statementRepository,
        IEmailService emailService,
        CommissionOptions options,
        ICardPaymentService cardPayments,
        IPaymentProofRepository paymentProofRepository,
        ClientOptions clientOptions,
        IPayoutService payouts,
        ISaleRepository sales)
    {
        _sales = sales;
        _payouts = payouts;
        _loanRepository = loanRepository;
        _itemRepository = itemRepository;
        _userRepository = userRepository;
        _statementRepository = statementRepository;
        _emailService = emailService;
        _options = options;
        _cardPayments = cardPayments;
        _paymentProofRepository = paymentProofRepository;
        _clientOptions = clientOptions;
    }

    // ===== שעון ישראל (גבולות החודש) =====
    // מקור אחד לשעון ישראל בכל המערכת (ראו IsraelTime)
    public static DateTime ToIsraelTime(DateTime utc) => IsraelTime.FromUtc(utc);

    public static string MonthKey(DateTime israelTime) => israelTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string CurrentMonthKey() => MonthKey(ToIsraelTime(DateTime.UtcNow));

    private static string NormalizeMonth(string? month) =>
        !string.IsNullOrWhiteSpace(month) && MonthPattern.IsMatch(month) ? month : CurrentMonthKey();

    private static List<string> RecentMonths(int count)
    {
        var now = ToIsraelTime(DateTime.UtcNow);
        return Enumerable.Range(0, count)
            .Select(i => MonthKey(new DateTime(now.Year, now.Month, 1).AddMonths(-i)))
            .ToList();
    }

    public static string HebrewMonthName(string month)
    {
        var year = int.Parse(month[..4], CultureInfo.InvariantCulture);
        var number = int.Parse(month[5..], CultureInfo.InvariantCulture);
        return $"{HebrewMonths[number - 1]} {year}";
    }

    // ===== חישוב =====
    private sealed record Computed(string OwnerId, string Month, CommissionRentalDto Rental);

    // כל ההשכרות שהוחזרו (לפי חודש ההחזרה בשעון ישראל), עם סכום הציוד והעמלה של כל אחת
    private async Task<List<Computed>> ComputeAllAsync()
    {
        var loans = await _loanRepository.GetAllAsync();
        var items = (await _itemRepository.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var defaultRate = _options.Percent / 100m;
        var result = new List<Computed>();

        foreach (var loan in loans)
        {
            if (loan.Status != LoanStatus.Returned || !loan.ActualReturnDate.HasValue)
                continue;
            if (loan.PaymentMethod == PaymentMethod.CreditCard)
                continue;
            if (!items.TryGetValue(loan.ItemCopyId ?? string.Empty, out var item) || string.IsNullOrEmpty(item.OwnerId))
                continue;

            var returnedLocal = ToIsraelTime(loan.ActualReturnDate.Value);
            var amount = (loan.RentalDays + loan.LateDays) * loan.RateOr(item.DailyPrice) * loan.Units;
            var rate = loan.CommissionRate ?? defaultRate;

            result.Add(new Computed(item.OwnerId, MonthKey(returnedLocal), new CommissionRentalDto
            {
                LoanId = loan.Id ?? string.Empty,
                ItemName = item.Name,
                StartDate = loan.StartDate,
                StartTime = loan.StartTime,
                ReturnedAt = returnedLocal,
                RentalDays = loan.RentalDays,
                LateDays = loan.LateDays,
                RentalAmount = amount,
                Rate = rate,
                Commission = Math.Round(amount * rate, 2, MidpointRounding.AwayFromZero)
            }));
        }

        foreach (var sale in await _sales.GetAllAsync())
        {
            if (sale.PaymentMethod == "CreditCard" || sale.Status != "Received" || sale.ReceivedAtUtc is null || string.IsNullOrEmpty(sale.SellerId))
                continue;
            var receivedLocal = ToIsraelTime(sale.ReceivedAtUtc.Value);
            result.Add(new Computed(sale.SellerId, MonthKey(receivedLocal), new CommissionRentalDto
            {
                LoanId = sale.Id ?? string.Empty,
                ItemName = $"יד 2: {sale.ItemName}",
                StartDate = ToIsraelTime(sale.CreatedAtUtc),
                ReturnedAt = receivedLocal,
                RentalDays = 0,
                RentalAmount = sale.Price,
                Rate = sale.CommissionRate,
                Commission = sale.Commission
            }));
        }

        return result;
    }

    private readonly ISaleRepository _sales;

    private static List<CommissionRentalDto> RentalsFor(IEnumerable<Computed> all, string ownerId, string month) =>
        all.Where(c => c.OwnerId == ownerId && c.Month == month)
           .Select(c => c.Rental)
           .OrderBy(r => r.ReturnedAt)
           .ToList();

    private static CommissionStatement? StatementFor(IEnumerable<CommissionStatement> statements, string ownerId, string month) =>
        statements.FirstOrDefault(s => s.OwnerId == ownerId && s.Month == month);

    private readonly IPayoutService _payouts;

    public async Task<CommissionReportDto> GetOwnerReportAsync(string ownerId, string? month)
    {
        month = NormalizeMonth(month);
        var all = await ComputeAllAsync();
        var statements = await _statementRepository.GetAllAsync();
        var rentals = RentalsFor(all, ownerId, month);
        var statement = StatementFor(statements, ownerId, month);

        return new CommissionReportDto
        {
            Month = month,
            Percent = _options.Percent,
            TotalRentals = rentals.Count,
            TotalRentalAmount = rentals.Sum(r => r.RentalAmount),
            TotalCommission = rentals.Sum(r => r.Commission),
            Paid = statement?.Paid ?? false,
            PaidAtUtc = statement?.PaidAtUtc,
            SentAtUtc = statement?.SentAtUtc,
            Rentals = rentals,
            RecentMonths = RecentMonths(6).Select(m => new CommissionMonthSummaryDto
            {
                Month = m,
                TotalCommission = RentalsFor(all, ownerId, m).Sum(r => r.Commission),
                Paid = StatementFor(statements, ownerId, m)?.Paid ?? false
            }).ToList()
        };
    }

    public async Task<CommissionAdminOverviewDto> GetAdminOverviewAsync(string? month)
    {
        month = NormalizeMonth(month);
        var all = await ComputeAllAsync();
        var statements = await _statementRepository.GetAllAsync();
        var users = (await _userRepository.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);

        var rows = new List<CommissionAdminRowDto>();
        foreach (var ownerId in all.Where(c => c.Month == month).Select(c => c.OwnerId).Distinct())
        {
            var rentals = RentalsFor(all, ownerId, month);
            var statement = StatementFor(statements, ownerId, month);
            users.TryGetValue(ownerId, out var owner);

            rows.Add(new CommissionAdminRowDto
            {
                OwnerId = ownerId,
                OwnerName = owner?.Name ?? "(משתמשת שנמחקה)",
                OwnerEmail = owner?.Email ?? string.Empty,
                OwnerPhone = owner?.Phone ?? string.Empty,
                TotalRentals = rentals.Count,
                TotalRentalAmount = rentals.Sum(r => r.RentalAmount),
                TotalCommission = rentals.Sum(r => r.Commission),
                Paid = statement?.Paid ?? false,
                PaidAtUtc = statement?.PaidAtUtc,
                SentAtUtc = statement?.SentAtUtc,
                OffsetAmount = statement?.OffsetAmount ?? 0,
                HasProof = !string.IsNullOrEmpty(statement?.ProofId),
                ProofUploadedAtUtc = statement?.ProofUploadedAtUtc,
                Rentals = rentals
            });
        }

        return new CommissionAdminOverviewDto
        {
            Month = month,
            Percent = _options.Percent,
            TotalCommission = rows.Sum(r => r.TotalCommission),
            PaidCommission = rows.Where(r => r.Paid).Sum(r => r.TotalCommission),
            UnpaidCommission = rows.Where(r => !r.Paid).Sum(r => r.TotalCommission),
            Months = RecentMonths(12),
            Owners = rows.OrderByDescending(r => r.TotalCommission).ToList()
        };
    }

    public async Task SetPaidAsync(CommissionMarkPaidDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.OwnerId) || !MonthPattern.IsMatch(dto.Month ?? string.Empty))
            throw new ArgumentException("בעלים או חודש לא תקינים");

        var statements = await _statementRepository.GetAllAsync();
        var statement = StatementFor(statements, dto.OwnerId, dto.Month!)
            ?? new CommissionStatement { OwnerId = dto.OwnerId, Month = dto.Month! };

        statement.Paid = dto.Paid;
        statement.PaidAtUtc = dto.Paid ? DateTime.UtcNow : null;
        await _statementRepository.UpsertAsync(statement);
        CommissionDebtService.InvalidateCache();
    }

    // ===== דוח במייל =====
    public async Task<CommissionEmailPreviewDto> PreviewReportAsync(string ownerId, string month)
    {
        if (!MonthPattern.IsMatch(month ?? string.Empty))
            throw new ArgumentException("חודש לא תקין");

        var owner = await _userRepository.GetByIdAsync(ownerId)
            ?? throw new KeyNotFoundException("המשכירה לא נמצאה");

        var all = await ComputeAllAsync();
        var rentals = RentalsFor(all, ownerId, month!);
        if (rentals.Count == 0)
            throw new ArgumentException("אין השכרות שהוחזרו בחודש הזה - אין מה להציג");

        return new CommissionEmailPreviewDto
        {
            ToEmail = owner.Email,
            ToName = owner.Name,
            Subject = $"דוח עמלה לחודש {HebrewMonthName(month!)} - common",
            Html = BuildReportHtml(owner, month!, rentals, _cardPayments.IsEnabled ? "#" : null, ProofPageUrl(ownerId, month!, null))
        };
    }

    public async Task<bool> SendReportAsync(string ownerId, string month)
    {
        if (!MonthPattern.IsMatch(month ?? string.Empty))
            throw new ArgumentException("חודש לא תקין");

        var owner = await _userRepository.GetByIdAsync(ownerId)
            ?? throw new KeyNotFoundException("המשכירה לא נמצאה");
        if (string.IsNullOrWhiteSpace(owner.Email))
            throw new InvalidOperationException("למשכירה אין כתובת מייל");

        var all = await ComputeAllAsync();
        var rentals = RentalsFor(all, ownerId, month!);
        if (rentals.Count == 0 || rentals.Sum(r => r.Commission) <= 0)
            return false;

        // בלי חיבור אמיתי לשירות המיילים (למשל בסביבת פיתוח) לא שולחים ולא מסמנים "נשלח"
        if (!_emailService.IsEnabled)
            throw new InvalidOperationException("שירות המיילים לא מוגדר בשרת (חסר מפתח Resend) - הדוח לא נשלח. אפשר לראות אותו בתצוגה מקדימה.");

        // טוקן סודי לקישורים שבמייל ("העלאת אסמכתא" ו"תשלום באשראי") - נוצר פעם אחת לכל דוח ונשמר בשליחה
        var statements = await _statementRepository.GetAllAsync();
        var statement = StatementFor(statements, ownerId, month!) ?? new CommissionStatement { OwnerId = ownerId, Month = month! };
        if (string.IsNullOrEmpty(statement.ProofToken))
            statement.ProofToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        // כפתור "לתשלום באשראי" מוביל לעמוד באתר שיוצר דף תשלום חדש בסאמיט עם הסכום המדויק (כך שהקישור לא פג)
        var paymentLink = _cardPayments.IsEnabled ? PayPageUrl(ownerId, month!, statement.ProofToken) : null;

        // חיוב אוטומטי של הכרטיס השמור, ברגע שליחת הדוח (רק למי שאישרה ושילמה כבר פעם בכרטיס). הניסיון נרשם *לפני* החיוב,
        // כדי שגם אם משהו נופל באמצע לעולם לא יחויב פעמיים. כישלון בחיוב לא עוצר את הדוח - נשלח עם קישור לתשלום רגיל.
        var outcome = AutoChargeOutcome.None;
        string? chargeError = null;
        var total = Math.Round(rentals.Sum(r => r.Commission), 2);

        // יש למשכירה כסף שממתין לה מהשכרות באשראי? העמלה מקוזזת ממנו, וההעברה אליה תהיה היתרה
        if (!statement.Paid && statement.OffsetAmount <= 0)
        {
            var balance = await _payouts.GetCardBalanceAsync(ownerId);
            var offset = Math.Round(Math.Min(balance, total), 2);
            if (offset > 0)
            {
                statement.OffsetAmount = offset;
                if (offset >= total)
                {
                    statement.Paid = true;
                    statement.PaidAtUtc = DateTime.UtcNow;
                }
                await _statementRepository.UpsertAsync(statement);
            }
        }
        var amountDue = Math.Max(0, total - statement.OffsetAmount);

        if (amountDue > 0 && owner.AutoChargeEnabled && _cardPayments.IsEnabled && !statement.Paid && statement.AutoChargeAttemptedAtUtc is null)
        {
            statement.AutoChargeAttemptedAtUtc = DateTime.UtcNow;
            await _statementRepository.UpsertAsync(statement);

            var charge = await _cardPayments.ChargeSavedCardAsync(new CardChargeRequest
            {
                Amount = amountDue,
                Description = $"עמלת שירות common - {HebrewMonthName(month!)}",
                CustomerName = owner.Name,
                Email = owner.Email,
                Phone = owner.Phone,
                CustomerExternalId = ownerId
            });

            if (charge.Success)
            {
                statement.Paid = true;
                statement.PaidAtUtc = DateTime.UtcNow;
                outcome = AutoChargeOutcome.Charged;
            }
            else
            {
                outcome = AutoChargeOutcome.Failed;
                chargeError = charge.Error;
            }
        }

        await _emailService.SendAsync(
            owner.Email, owner.Name, $"דוח עמלה לחודש {HebrewMonthName(month!)} - common",
            BuildReportHtml(owner, month!, rentals, statement.Paid && outcome != AutoChargeOutcome.Charged ? null : paymentLink,
                ProofPageUrl(ownerId, month!, statement.ProofToken), outcome, owner.AutoChargeEnabled, statement.OffsetAmount));

        statement.SentAtUtc = DateTime.UtcNow;
        await _statementRepository.UpsertAsync(statement);
        CommissionDebtService.InvalidateCache();
        return true;
    }

    private string PayPageUrl(string ownerId, string month, string token) =>
        $"{_clientOptions.BaseUrl.TrimEnd('/')}/commission-pay?o={Uri.EscapeDataString(ownerId)}&m={Uri.EscapeDataString(month)}&t={token}";

    private string ProofPageUrl(string ownerId, string month, string? token)
    {
        var url = $"{_clientOptions.BaseUrl.TrimEnd('/')}/commission-proof?o={Uri.EscapeDataString(ownerId)}&m={Uri.EscapeDataString(month)}";
        return string.IsNullOrEmpty(token) ? url : $"{url}&t={token}";
    }

    private static bool TokenMatches(CommissionStatement? statement, string? token) =>
        statement is not null &&
        !string.IsNullOrEmpty(statement.ProofToken) &&
        !string.IsNullOrEmpty(token) &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(statement.ProofToken),
            Encoding.UTF8.GetBytes(token));

    // ===== תשלום באשראי (סאמיט) =====
    private const string CardIdentifierPrefix = "commission|";

    public async Task<string?> BeginCardPaymentAsync(string ownerId, string month, string? token, bool consent)
    {
        if (!MonthPattern.IsMatch(month ?? string.Empty)) return null;

        var statements = await _statementRepository.GetAllAsync();
        var statement = StatementFor(statements, ownerId, month!);
        if (!TokenMatches(statement, token)) return null;

        if (!_cardPayments.IsEnabled)
            throw new InvalidOperationException("התשלום באשראי אינו זמין כרגע - אפשר לשלם בהעברה בנקאית (הפרטים במייל)");
        if (statement!.Paid)
            throw new InvalidOperationException("הדוח הזה כבר שולם. תודה!");

        var owner = await _userRepository.GetByIdAsync(ownerId);
        if (owner is null) return null;

        // ההסכמה חובה: בלעדיה לא ממשיכים לתשלום באשראי (אפשר לשלם בהעברה בנקאית)
        if (!consent)
            throw new InvalidOperationException("כדי לשלם באשראי יש לאשר את שמירת הכרטיס ואת החיוב החודשי האוטומטי. אפשר גם לשלם בהעברה בנקאית.");

        var all = await ComputeAllAsync();
        var total = Math.Round(RentalsFor(all, ownerId, month!).Sum(r => r.Commission), 2) - statement.OffsetAmount;
        if (total <= 0)
            throw new InvalidOperationException("אין עמלה לתשלום בחודש הזה");

        // רושמים את ההסכמה (עם תאריך); החיוב האוטומטי מופעל רק אחרי שהתשלום הראשון הושלם והכרטיס נשמר
        await _userRepository.UpdateAutoChargeAsync(ownerId, DateTime.UtcNow, owner.AutoChargeEnabled);

        var baseUrl = _clientOptions.BaseUrl.TrimEnd('/');
        return await _cardPayments.BeginPaymentAsync(new CardPaymentRequest
        {
            Amount = total,
            Description = $"עמלת שירות common - {HebrewMonthName(month!)}",
            CustomerName = owner.Name,
            Email = owner.Email,
            Phone = owner.Phone,
            CustomerExternalId = ownerId,
            ExternalIdentifier = $"{CardIdentifierPrefix}{ownerId}|{month}",
            RedirectUrl = $"{baseUrl}/commission-paid",
            CancelUrl = PayPageUrl(ownerId, month!, statement.ProofToken!),
            SavePaymentMethod = true
        });
    }

    public async Task<CommissionCardConfirmResultDto> ConfirmCardPaymentAsync(CommissionCardConfirmDto dto)
    {
        var notConfirmed = new CommissionCardConfirmResultDto { Paid = false };
        if (!_cardPayments.IsEnabled || !long.TryParse(dto.PaymentId, NumberStyles.None, CultureInfo.InvariantCulture, out var paymentId))
            return notConfirmed;

        // אימות מול סאמיט (בין שרת לשרת) - לא סומכים על מה שהדפדפן שלח
        var info = await _cardPayments.GetPaymentAsync(paymentId);
        if (info is null || !info.Valid || string.IsNullOrEmpty(info.ExternalIdentifier)
            || !info.ExternalIdentifier.StartsWith(CardIdentifierPrefix, StringComparison.Ordinal))
            return notConfirmed;

        // המזהה שנשמר בתשלום עצמו הוא הקובע (ולא זה שבכתובת); אם הדפדפן שלח מזהה אחר - מסרבים
        if (!string.IsNullOrEmpty(dto.ExternalIdentifier) && dto.ExternalIdentifier != info.ExternalIdentifier)
            return notConfirmed;

        var parts = info.ExternalIdentifier[CardIdentifierPrefix.Length..].Split('|');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !MonthPattern.IsMatch(parts[1]))
            return notConfirmed;
        var ownerId = parts[0];
        var month = parts[1];

        var all = await ComputeAllAsync();
        var total = Math.Round(RentalsFor(all, ownerId, month).Sum(r => r.Commission), 2);
        if (total <= 0 || info.Amount + 0.01m < total)
            return notConfirmed;

        await SetPaidAsync(new CommissionMarkPaidDto { OwnerId = ownerId, Month = month, Paid = true });

        // תשלום ראשון בכרטיס אחרי הסכמה = הכרטיס נשמר, ומעכשיו הדוחות החודשיים יחויבו אוטומטית
        var payer = await _userRepository.GetByIdAsync(ownerId);
        if (payer is { AutoChargeConsentAtUtc: not null, AutoChargeEnabled: false })
            await _userRepository.UpdateAutoChargeAsync(ownerId, payer.AutoChargeConsentAtUtc, true);

        return new CommissionCardConfirmResultDto { Paid = true, MonthLabel = HebrewMonthName(month), Amount = info.Amount };
    }

    public async Task<bool> ResendReportAsync(string ownerId, string month, string? token)
    {
        if (!MonthPattern.IsMatch(month ?? string.Empty)) return false;

        var statement = StatementFor(await _statementRepository.GetAllAsync(), ownerId, month!);
        if (!TokenMatches(statement, token)) return false;

        var owner = await _userRepository.GetByIdAsync(ownerId);
        if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
            throw new InvalidOperationException("לא נמצאה כתובת מייל לשליחת הדוח");
        if (!_emailService.IsEnabled)
            throw new InvalidOperationException("שליחת מיילים אינה זמינה כרגע - נסי שוב מאוחר יותר");

        var rentals = RentalsFor(await ComputeAllAsync(), ownerId, month!);
        var paymentLink = _cardPayments.IsEnabled && !statement!.Paid ? PayPageUrl(ownerId, month!, statement.ProofToken!) : null;
        await _emailService.SendAsync(
            owner.Email, owner.Name, $"דוח עמלה לחודש {HebrewMonthName(month!)} - common",
            BuildReportHtml(owner, month!, rentals, paymentLink, ProofPageUrl(ownerId, month!, statement!.ProofToken),
                AutoChargeOutcome.None, owner.AutoChargeEnabled, statement.OffsetAmount));
        return true;
    }

    public async Task<CommissionProofViewDto?> GetProofViewAsync(string ownerId, string month, string? token)
    {
        if (!MonthPattern.IsMatch(month ?? string.Empty)) return null;

        var statement = StatementFor(await _statementRepository.GetAllAsync(), ownerId, month!);
        if (!TokenMatches(statement, token)) return null;

        var owner = await _userRepository.GetByIdAsync(ownerId);
        var rentals = RentalsFor(await ComputeAllAsync(), ownerId, month!);
        return new CommissionProofViewDto
        {
            OwnerName = owner?.Name ?? string.Empty,
            Month = month!,
            MonthLabel = HebrewMonthName(month!),
            Amount = rentals.Sum(r => r.Commission),
            AlreadyUploaded = !string.IsNullOrEmpty(statement!.ProofId),
            Paid = statement.Paid
        };
    }

    public async Task<bool> SubmitProofAsync(string ownerId, string month, string? token, CommissionProofUploadDto dto)
    {
        if (!MonthPattern.IsMatch(month ?? string.Empty)) return false;

        var statement = StatementFor(await _statementRepository.GetAllAsync(), ownerId, month!);
        if (!TokenMatches(statement, token)) return false;

        var proof = ProofFileValidator.Validate(dto.DataUrl, dto.FileName);
        var saved = await _paymentProofRepository.CreateAsync(new PaymentProof
        {
            FileName = proof.FileName,
            ContentType = proof.ContentType,
            DataUrl = dto.DataUrl!,
            UploadedAtUtc = DateTime.UtcNow
        });

        statement!.ProofId = saved.Id;
        statement.ProofUploadedAtUtc = DateTime.UtcNow;
        await _statementRepository.UpsertAsync(statement);

        // הודעה לתיבת האתר עם האסמכתא מצורפת (כשל במייל לא מכשיל את ההעלאה - האסמכתא כבר נשמרה)
        try
        {
            var owner = await _userRepository.GetByIdAsync(ownerId);
            var amount = RentalsFor(await ComputeAllAsync(), ownerId, month!).Sum(r => r.Commission);
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>{WebUtility.HtmlEncode(owner?.Name ?? ownerId)} העלתה אסמכתת העברה עבור דוח העמלה של {HebrewMonthName(month!)} (₪{amount.ToString("0.00", CultureInfo.InvariantCulture)}).</p>" +
                       "<p>האסמכתא מצורפת, ואפשר לראות אותה גם במסך הניהול (עמלות). לא סומן אוטומטית כשולם - יש לסמן אחרי שהכסף נכנס.</p></div>";
            await _emailService.SendAsync(
                InboxEmail, "common", $"אסמכתת העברה: {owner?.Name} - {HebrewMonthName(month!)}", html,
                new[] { new EmailAttachment(proof.FileName, proof.Base64) }, owner?.Email);
        }
        catch
        {
            // ממשיכים
        }

        return true;
    }

    public async Task<PaymentProofDto?> GetProofAsync(string ownerId, string month)
    {
        var statement = StatementFor(await _statementRepository.GetAllAsync(), ownerId, month);
        if (statement is null || string.IsNullOrEmpty(statement.ProofId)) return null;

        var proof = await _paymentProofRepository.GetByIdAsync(statement.ProofId);
        return proof is null ? null : new PaymentProofDto { FileName = proof.FileName, ContentType = proof.ContentType, DataUrl = proof.DataUrl };
    }

    public async Task SendMonthlyReportsAsync(string month)
    {
        if (!MonthPattern.IsMatch(month))
            return;

        var all = await ComputeAllAsync();
        var statements = await _statementRepository.GetAllAsync();

        foreach (var ownerId in all.Where(c => c.Month == month).Select(c => c.OwnerId).Distinct())
        {
            if (StatementFor(statements, ownerId, month)?.SentAtUtc is not null)
                continue;

            try
            {
                await SendReportAsync(ownerId, month);
            }
            catch
            {
                // כשל אצל משכירה אחת לא עוצר את שאר הדוחות; ננסה שוב בסבב הבא
            }
        }
    }

    // אזור "איך לשלם" בדוח: כפתור תשלום באשראי (אם הוגדר קישור) ופרטי חשבון להעברה בנקאית (אם הוגדרו).
    // אם לא הוגדר דבר - נשארת ההנחיה הכללית להשיב למייל.
    private string BuildPaymentHtml(User owner, string month, decimal totalCommission, string? dynamicLink)
    {
        static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

        var sb = new StringBuilder();
        // קישור התשלום באשראי (דף באתר שיוצר דף תשלום עם הסכום המדויק) עדיף על הקישור הקבוע שבהגדרות
        var link = !string.IsNullOrWhiteSpace(dynamicLink) ? dynamicLink.Trim() : _options.PaymentLinkUrl?.Trim() ?? string.Empty;
        if (link == "#") link = "https://commonrent.web.app/?preview-payment-link";
        var hasLink = Uri.TryCreate(link, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        var hasBank = !string.IsNullOrWhiteSpace(_options.BankDetails);
        var amount = "₪" + totalCommission.ToString("0.00", CultureInfo.InvariantCulture);

        if (hasLink || hasBank)
        {
            sb.Append("<div style=\"border:2px solid #000;border-radius:12px;padding:12px;margin:12px 0\">");
            sb.Append($"<p style=\"margin:0 0 8px\"><strong>איך לשלם ({amount}):</strong></p>");
            if (hasLink)
            {
                sb.Append($"<p style=\"margin:0 0 10px\"><a href=\"{H(uri!.ToString())}\" style=\"display:inline-block;background:#e89c9c;color:#000;border:1.5px solid #000;border-radius:999px;padding:9px 20px;text-decoration:none;font-weight:bold\">לתשלום באשראי - לחצי כאן</a></p>");
            }
            if (hasBank)
            {
                var bank = H(_options.BankDetails).Replace("|", "<br/>").Replace("\n", "<br/>");
                sb.Append($"<p style=\"margin:0\"><strong>{(hasLink ? "או בהעברה בנקאית" : "בהעברה בנקאית")}:</strong><br/>{bank}</p>");
                sb.Append($"<p style=\"margin:6px 0 0;font-size:13px\">בהעברה נא לציין: common {H(HebrewMonthName(month))} - {H(owner.Name)}</p>");
            }
            sb.Append("</div>");
        }
        else
        {
            sb.Append("<p>לתשלום ניתן לשלם בהעברה בנקאית או בקישור תשלום - השיבי למייל הזה ונשלח לך את הפרטים.</p>");
        }

        if (!string.IsNullOrWhiteSpace(_options.PaymentInstructions))
            sb.Append($"<p>{H(_options.PaymentInstructions).Replace("\n", "<br/>")}</p>");

        return sb.ToString();
    }

    private string BuildReportHtml(User owner, string month, List<CommissionRentalDto> rentals, string? paymentLink = null, string? proofLink = null,
        AutoChargeOutcome outcome = AutoChargeOutcome.None, bool autoChargeEnabled = false, decimal offset = 0)
    {
        static string H(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);
        static string Money(decimal value) => "₪" + value.ToString("0.00", CultureInfo.InvariantCulture);

        var totalAmount = rentals.Sum(r => r.RentalAmount);
        var totalCommission = rentals.Sum(r => r.Commission);
        var due = ToIsraelTime(DateTime.UtcNow).AddDays(14).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var rows = new StringBuilder();
        foreach (var r in rentals)
        {
            var start = r.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(r.StartTime) ? string.Empty : " " + r.StartTime);
            rows.Append("<tr>")
                .Append($"<td style=\"border:1px solid #000\">{H(r.ItemName)}</td>")
                .Append($"<td style=\"border:1px solid #000\">{H(start)}</td>")
                .Append($"<td style=\"border:1px solid #000\">{r.ReturnedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}</td>")
                .Append($"<td style=\"border:1px solid #000\">{r.RentalDays}</td>")
                .Append($"<td style=\"border:1px solid #000\">{Money(r.RentalAmount)}</td>")
                .Append($"<td style=\"border:1px solid #000\">{(r.Rate * 100).ToString("0.##", CultureInfo.InvariantCulture)}%</td>")
                .Append($"<td style=\"border:1px solid #000\"><strong>{Money(r.Commission)}</strong></td>")
                .Append("</tr>");
        }

        var amountDue = Math.Max(0, totalCommission - offset);
        var instructions = BuildPaymentHtml(owner, month, amountDue, paymentLink);
        var offsetNote = offset > 0
            ? "<div style=\"border:2px solid #000;border-radius:12px;padding:12px;margin:12px 0;background:#e4f6df\">" +
              $"<p style=\"margin:0\">✓ <strong>{Money(offset)} מהעמלה קוזזו</strong> מהתשלומים שמגיעים לך על השכרות ששולמו באשראי דרך האתר. " +
              "היתרה תועבר לחשבון הבנק שלך, וכל הפירוט מתועד אצלנו.</p></div>"
            : string.Empty;

        string payBlock;
        if (offset > 0 && amountDue <= 0)
        {
            payBlock = offsetNote + "<p><strong>אין צורך בתשלום נוסף.</strong></p>";
        }
        else if (outcome == AutoChargeOutcome.Charged)
        {
            payBlock = "<div style=\"border:2px solid #000;border-radius:12px;padding:12px;margin:12px 0;background:#e4f6df\">" +
                       $"<p style=\"margin:0\">✓ <strong>הכרטיס השמור שלך חויב ב-{Money(amountDue)}</strong>, לפי ההרשאה שנתת לחיוב חודשי אוטומטי. " +
                       "חשבונית/קבלה נשלחת אלייך ישירות ממערכת הסליקה. אין צורך בפעולה נוספת.</p></div>";
        }
        else
        {
            var failNote = outcome == AutoChargeOutcome.Failed
                ? "<div style=\"border:2px solid #c00;border-radius:12px;padding:10px;margin:12px 0;background:#ffe2e2\"><strong>ניסינו לחייב את הכרטיס השמור שלך ולא הצלחנו.</strong> אפשר לשלם בקישור שלמטה.</div>"
                : string.Empty;
            payBlock = offsetNote + failNote +
                       $"<p><strong>סכום לתשלום: {Money(amountDue)}</strong> - עד {due}.</p>" +
                       instructions +
                       (string.IsNullOrWhiteSpace(proofLink)
                           ? "<p>נא לצרף אסמכתת העברה כתשובה למייל.</p>"
                           : $"<p>נא לצרף אסמכתת העברה כתשובה למייל או <a href=\"{H(proofLink)}\">כאן</a>.</p>") +
                       "<p>לאחר קבלת התשלום תישלח אליך קבלה.</p>";
        }

        var autoChargeNote = autoChargeEnabled
            ? "<p style=\"font-size:13px\">הדוחות החודשיים מחויבים אוטומטית בכרטיס השמור, לפי ההרשאה שנתת. לשאלות או לבקשות בנוגע להרשאה - אפשר להשיב למייל הזה.</p>"
            : string.Empty;

        // מה הציוד הכניס לה בחודש (סכום הציוד פחות העמלה)
        var times = rentals.Count == 1 ? "פעם אחת" : $"{rentals.Count} פעמים";
        var earned = "₪" + (totalAmount - totalCommission).ToString("#,0.##", CultureInfo.InvariantCulture);
        var earningsLine = $"<p style=\"font-size:17px;background:#e4f6df;border:1px solid #000;border-radius:14px;padding:12px 14px\">" +
                           $"<strong>בחודש {HebrewMonthName(month)} הציוד שלך הושכר {times} והכניס לך {earned}</strong></p>";

        return $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
               $"<p>שלום {H(NameText.FirstName(owner.Name))},</p>" +
               earningsLine +
               $"<p>זה דוח עמלת השירות של common לחודש <strong>{HebrewMonthName(month)}</strong>. העמלה היא {_options.Percent.ToString("0.##", CultureInfo.InvariantCulture)}% מסכום הציוד בכל השכרה שהוחזרה בחודש זה (ללא דמי משלוח).</p>" +
               "<table style=\"border-collapse:collapse\" cellpadding=\"6\">" +
               "<tr><th style=\"border:1px solid #000\">פריט</th><th style=\"border:1px solid #000\">תחילת השכרה</th><th style=\"border:1px solid #000\">הוחזר</th><th style=\"border:1px solid #000\">ימים</th><th style=\"border:1px solid #000\">סכום הציוד</th><th style=\"border:1px solid #000\">עמלה</th><th style=\"border:1px solid #000\">לתשלום</th></tr>" +
               rows +
               $"<tr><td colspan=\"4\"><strong>סה\"כ ({rentals.Count} השכרות)</strong></td><td><strong>{Money(totalAmount)}</strong></td><td></td><td><strong>{Money(totalCommission)}</strong></td></tr>" +
               "</table>" +
               payBlock +
               $"<p>את הפירוט אפשר לראות בכל עת <a href=\"{H(_clientOptions.BaseUrl.TrimEnd('/'))}/profile\">באזור האישי</a> באתר.</p>" +
               autoChargeNote +
               "</div>";
    }
}
