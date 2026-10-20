namespace PhotoRental.Service.Services;

using System.Globalization;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Exceptions;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// שכבת הלוגיקה העסקית לניהול עסקאות השכרה
public class LoanService : ILoanService
{
    private readonly ILoanRepository _loanRepository;
    private readonly IItemRepository _itemRepository;
    private readonly IUserRepository _userRepository;
    private readonly IShippingService _shippingService;
    private readonly IEmailService _emailService;
    private readonly CommissionOptions _commissionOptions;
    private readonly IFavoriteRepository _favorites;
    private readonly IPaymentProofRepository _paymentProofRepository;
    private readonly ClientOptions _clientOptions;
    private readonly IOutboundCallService _outboundCalls;

    // תיבת הדואר של האתר - לשם נשלחים דיווחי "הפריט לא הוחזר" לטיפול הצוות
    private const string InboxEmail = "common@onyxapp.co.il";

    private readonly IShippingPaymentService _shippingPayments;
    private readonly IOrderPaymentService _orderPayments;

    private readonly IExtraChargeService _extraCharges;

    public LoanService(
        IFavoriteRepository favorites,
        ILoanRepository loanRepository,
        IItemRepository itemRepository,
        IUserRepository userRepository,
        IShippingService shippingService,
        IEmailService emailService,
        CommissionOptions commissionOptions,
        IPaymentProofRepository paymentProofRepository,
        ClientOptions clientOptions,
        IOutboundCallService outboundCalls,
        IShippingPaymentService shippingPayments,
        IOrderPaymentService orderPayments,
        IExtraChargeService extraCharges,
        IMarkerRepository markers,
        IItemScheduleService schedule)
    {
        _favorites = favorites;
        _extraCharges = extraCharges;
        _markers = markers;
        _schedule = schedule;
        _outboundCalls = outboundCalls;
        _shippingPayments = shippingPayments;
        _orderPayments = orderPayments;
        _loanRepository = loanRepository;
        _itemRepository = itemRepository;
        _userRepository = userRepository;
        _shippingService = shippingService;
        _emailService = emailService;
        _commissionOptions = commissionOptions;
        _paymentProofRepository = paymentProofRepository;
        _clientOptions = clientOptions;
    }

    private const int MaxProofChars = 900_000;

    // קישור "באזור האישי" בתוך מיילים - מוביל לדף החשבון באתר
    private string ProfileLink => $"<a href=\"{_clientOptions.BaseUrl.TrimEnd('/')}/profile\">באזור האישי</a>";

    // בהעברה בנקאית חובה להעלות אסמכתא (תמונה או PDF) לפני אישור ההשכרה. הקובץ נשמר בנפרד
    // (PaymentProofs) ומוחזר גם כקובץ מצורף שיישלח במייל למשכירה.
    private async Task<(string ProofId, EmailAttachment Attachment)> StoreProofAsync(string? dataUrl, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
            throw new ArgumentException("יש להעלות אסמכתא להעברה הבנקאית לפני אישור ההשכרה");
        if (dataUrl.Length > MaxProofChars)
            throw new ArgumentException("קובץ האסמכתא גדול מדי - נא להעלות תמונה או PDF קטנים יותר");

        var comma = dataUrl.IndexOf(',');
        var header = comma > 0 ? dataUrl[..comma] : string.Empty;
        var extensionByType = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif", ["application/pdf"] = ".pdf",
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx", ["application/vnd.ms-excel"] = ".xls", ["text/csv"] = ".csv"
        };

        var contentType = header.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
            ? header["data:".Length..^";base64".Length]
            : string.Empty;
        if (!extensionByType.TryGetValue(contentType, out var extension))
            throw new ArgumentException("קובץ האסמכתא חייב להיות תמונה, PDF או קובץ אקסל");

        var base64 = dataUrl[(comma + 1)..];
        if (!Convert.TryFromBase64String(base64, new byte[base64.Length], out _))
            throw new ArgumentException("קובץ האסמכתא פגום - נא להעלות אותו שוב");

        var safeName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "asmachta" + extension;
        else if (!Path.HasExtension(safeName))
            safeName += extension;
        if (safeName.Length > 80)
            safeName = safeName[^80..];

        var saved = await _paymentProofRepository.CreateAsync(new PaymentProof
        {
            FileName = safeName,
            ContentType = contentType,
            DataUrl = dataUrl,
            UploadedAtUtc = DateTime.UtcNow
        });

        return (saved.Id!, new EmailAttachment(safeName, base64));
    }

    // limit: רק ההשכרות האחרונות (לפי תאריך התחלה), כדי לא לעבד את כל ההיסטוריה בכל טעינה
    private static IEnumerable<Loan> Latest(IEnumerable<Loan> loans, int? limit)
    {
        var ordered = loans.OrderByDescending(l => l.StartDate).ThenByDescending(l => l.LoanDate);
        return limit is > 0 ? ordered.Take(limit.Value) : ordered;
    }

    public async Task<IReadOnlyList<LoanResponseDto>> GetByOwnerIdAsync(string ownerId, int? limit = null)
    {
        var itemIds = (await _itemRepository.GetByOwnerIdAsync(ownerId)).Where(i => !string.IsNullOrEmpty(i.Id)).Select(i => i.Id!).ToHashSet();
        var loans = Latest((await _loanRepository.GetAllAsync()).Where(l => l.ItemCopyId is not null && itemIds.Contains(l.ItemCopyId)), limit);

        var result = new List<LoanResponseDto>();
        foreach (var loan in loans)
        {
            var dto = await MapToResponseAsync(loan);
            if (loan.PaymentMethod == PaymentMethod.CreditCard)
            {
                var gross = PayoutMath.Gross(loan, loan.RateOr(loan.Item?.DailyPrice ?? 0));
                var rate = loan.CommissionRate ?? _commissionOptions.Percent / 100m;
                dto.OwnerPayoutNet = gross - PayoutMath.Commission(gross, rate);
                dto.OwnerPayoutRelease = loan.Status == LoanStatus.Returned ? PayoutMath.ReleaseAt(loan) : null;
                dto.OwnerPayoutPaid = loan.OwnerPayoutPaidAtUtc is not null;
                dto.OwnerPayoutHasProof = !string.IsNullOrEmpty(loan.OwnerPayoutProofId);
            }
            result.Add(dto);
        }
        return result;
    }

    // ציון "המבוקשים בקהילה": השכרה שלא בוטלה = 2 נקודות, שמירה במועדפים = נקודה
    public async Task<Dictionary<string, int>> GetRentalCountsAsync()
    {
        return await CatalogCache.GetOrAddAsync("popularity", TimeSpan.FromSeconds(60), async () =>
        {
            var score = new Dictionary<string, int>();
            foreach (var loan in await _loanRepository.GetAllAsync())
            {
                if (string.IsNullOrEmpty(loan.ItemCopyId) || loan.Status == LoanStatus.Cancelled)
                    continue;
                score[loan.ItemCopyId] = score.GetValueOrDefault(loan.ItemCopyId) + 2;
            }
            foreach (var favorite in await _favorites.GetAllAsync())
            {
                if (!favorite.IsFavorite || string.IsNullOrEmpty(favorite.ItemId))
                    continue;
                score[favorite.ItemId] = score.GetValueOrDefault(favorite.ItemId) + 1;
            }
            return score;
        });
    }

    public async Task<LoanParticipants?> GetParticipantsAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId);
        if (loan is null) return null;

        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _itemRepository.GetByIdAsync(loan.ItemCopyId) : null;
        return new LoanParticipants(loan.BorrowerId, item?.OwnerId);
    }

    private static string NewActionToken() =>
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public async Task<bool> ValidateOwnerTokenAsync(string loanId, string? token)
    {
        if (string.IsNullOrWhiteSpace(loanId) || string.IsNullOrWhiteSpace(token))
            return false;

        var loan = await _loanRepository.GetByIdAsync(loanId);
        if (loan is null || string.IsNullOrEmpty(loan.OwnerActionToken))
            return false;

        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(loan.OwnerActionToken),
            System.Text.Encoding.UTF8.GetBytes(token));
    }

    // טוקן השוכרת נגזר מטוקן המשכירה (HMAC), כך שאין צורך לשמור עוד שדה סודי
    public static string? RenterActionToken(Loan loan)
    {
        if (string.IsNullOrEmpty(loan.OwnerActionToken) || string.IsNullOrEmpty(loan.Id))
            return null;
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(loan.OwnerActionToken));
        var hash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"renter:{loan.Id}"));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public async Task<bool> ValidateRenterTokenAsync(string loanId, string? token)
    {
        if (string.IsNullOrWhiteSpace(loanId) || string.IsNullOrWhiteSpace(token))
            return false;
        var loan = await _loanRepository.GetByIdAsync(loanId);
        var expected = loan is null ? null : RenterActionToken(loan);
        return expected is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(token));
    }

    public async Task<RenterPickupDto?> GetRenterPickupViewAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId);
        if (loan is null) return null;
        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _itemRepository.GetByIdAsync(loan.ItemCopyId) : null;
        return new RenterPickupDto
        {
            LoanId = loan.Id ?? string.Empty,
            ItemName = item is null ? string.Empty : LoanText.Name(item, loan),
            DeliveryMethod = loan.DeliveryMethod.ToString(),
            Status = loan.Status.ToString(),
            PickedUpAt = loan.PickedUpAt,
            PickedUpBy = loan.PickedUpBy
        };
    }

    public async Task<LoanActionDto?> GetActionViewAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId);
        if (loan is null) return null;

        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _itemRepository.GetByIdAsync(loan.ItemCopyId) : null;
        var owner = !string.IsNullOrEmpty(item?.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
        var proof = !string.IsNullOrEmpty(loan.PaymentProofId) ? await _paymentProofRepository.GetByIdAsync(loan.PaymentProofId) : null;

        return new LoanActionDto
        {
            LoanId = loan.Id ?? string.Empty,
            ItemName = item is null ? string.Empty : LoanText.Name(item, loan),
            OwnerName = owner?.Name ?? string.Empty,
            StartDate = loan.StartDate,
            StartTime = loan.StartTime,
            ExpectedReturnDate = DateTime.SpecifyKind(loan.ExpectedReturnDate, DateTimeKind.Unspecified),
            DeliveryMethod = loan.DeliveryMethod.ToString(),
            PaymentMethod = loan.PaymentMethod.ToString(),
            PaymentStatus = loan.PaymentStatus.ToString(),
            Amount = loan.RentalDays * loan.RateOr(item?.DailyPrice ?? 0) * loan.Units,
            PickedUpAt = loan.PickedUpAt,
            PickedUpBy = loan.PickedUpBy,
            ExtensionStatus = loan.ExtensionStatus,
            ExtensionDays = Math.Max(1, loan.ExtensionDays),
            ExtensionNewReturn = loan.ExtensionStatus == "Pending"
                ? DateTime.SpecifyKind(loan.ExpectedReturnDate.AddDays(Math.Max(1, loan.ExtensionDays)), DateTimeKind.Unspecified)
                : null,
            Status = loan.Status.ToString(),
            ActualReturnDate = loan.ActualReturnDate,
            NotReturnedReportedAt = loan.NotReturnedReportedAt,
            DailyPrice = loan.RateOr(item?.DailyPrice ?? 0),
            IsLocation = loan.IsLocation,
            HasPaymentProof = proof is not null,
            PaymentProofFileName = proof?.FileName
        };
    }

    public async Task<IReadOnlyList<LoanResponseDto>> GetOrderAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId)
            ?? throw new KeyNotFoundException("ההזמנה לא נמצאה");

        var loans = string.IsNullOrEmpty(loan.OrderGroupId)
            ? new List<Loan> { loan }
            : (await _loanRepository.GetAllAsync()).Where(l => l.OrderGroupId == loan.OrderGroupId).ToList();

        var result = new List<LoanResponseDto>();
        foreach (var l in loans)
            result.Add(await MapToResponseAsync(l));
        return result;
    }

    // שינוי אופן ההחזרה בזמן ההשכרה. הזמנה מרובת פריטים חולקת איסוף אחד, ולכן השינוי חל על כולה.
    // מעבר לשליח - מזמינים איסוף חדש; מעבר למסירה עצמית (או החלפת כתובת) - מבטלים את האיסוף הקודם.
    // אי אפשר לשנות אחרי שהשליח כבר בדרך לאסוף או אסף
    public async Task<LoanResponseDto> ChangeReturnMethodAsync(string loanId, ChangeReturnMethodDto request)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        if (loan.Status != LoanStatus.Active)
            throw new InvalidOperationException("אפשר לשנות את אופן ההחזרה רק בהשכרה פעילה");

        var newMethod = ParseReturnMethod(request.ReturnMethod);
        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _itemRepository.GetByIdAsync(loan.ItemCopyId) : null;
        if (item is null)
            throw new KeyNotFoundException("הפריט לא נמצא");

        var group = string.IsNullOrEmpty(loan.OrderGroupId)
            ? new List<Loan> { loan }
            : (await _loanRepository.GetAllAsync()).Where(l => l.OrderGroupId == loan.OrderGroupId && l.Status == LoanStatus.Active).ToList();

        if (newMethod == ReturnMethod.SelfDropoff && loan.ReturnMethod == ReturnMethod.SelfDropoff)
            return await MapToResponseAsync(loan);

        // שליח שכבר בדרך לאסוף / אסף - אי אפשר לבטל או להחליף
        var oldProviderId = loan.ReturnMethod == ReturnMethod.CourierPickup ? loan.ReturnProviderId : null;
        if (!string.IsNullOrEmpty(oldProviderId))
        {
            ShipmentStatusDto? current = null;
            try { current = await _shippingService.GetStatusAsync(oldProviderId); }
            catch (InvalidOperationException) { /* לא הצלחנו לקרוא מצב - הביטול עצמו יכשל אם אי אפשר */ }

            if (current?.Status is "OnTheWay" or "PickedUp" or "Delivered")
                throw new InvalidOperationException("השליח כבר בדרך לאסוף את הפריט, ולכן אי אפשר לשנות את אופן ההחזרה. אפשר לתאם איתו ישירות.");
        }

        string? newTracking = null, newProviderId = null, newAddressText = null;
        var newReturnFee = 0m;

        if (newMethod == ReturnMethod.CourierPickup)
        {
            if (!IsCourierAddressComplete(request.ReturnPickupAddress))
                throw new ArgumentException("יש להזין כתובת מלאה לאיסוף ההחזרה: עיר, רחוב, מספר בית, קומה ומספר דירה או שם משפחה");
            if (loan.ExpectedReturnDate - IsraelTime.Now < TimeSpan.FromMinutes(30))
                throw new InvalidOperationException("מאוחר מדי להזמין שליח להחזרה - נותרו פחות מ-30 דקות. אפשר להחזיר בעצמך למשכירה.");

            var owner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
            RequireOwnerCourierDetails(owner);
            var ownerAddress = OwnerShippingAddress($"{item.Address}, {item.City}".Trim(',', ' '), owner);
            var pickupAddress = ShippingAddressDto.FromDetails(request.ReturnPickupAddress!);

            // קודם מזמינים את החדש - אם נכשל, האיסוף הקודם נשאר כמו שהוא
            var renter = !string.IsNullOrEmpty(loan.BorrowerId) ? await _userRepository.GetByIdAsync(loan.BorrowerId) : null;
            var pickup = await _shippingService.RequestPickupAsync(pickupAddress.WithContact(renter?.Name, renter?.Phone), ownerAddress.WithContact(owner?.Name, owner?.Phone), loan.ExpectedReturnDate, isExpress: false, loan.ExpectedReturnDate.TimeOfDay);
            newTracking = pickup.TrackingRef;
            newProviderId = pickup.ProviderId;
            newAddressText = FormatDeliveryAddress(request.ReturnPickupAddress!);
            newReturnFee = (await _shippingService.GetQuoteAsync(ownerAddress, pickupAddress)).RegularPrice;

            if (!string.IsNullOrEmpty(oldProviderId))
            {
                try
                {
                    await _shippingService.CancelAsync(oldProviderId);
                }
                catch
                {
                    // לא מצליחים לבטל את הישן - מבטלים את החדש כדי לא להשאיר שני שליחים
                    if (!string.IsNullOrEmpty(newProviderId))
                    {
                        try { await _shippingService.CancelAsync(newProviderId); } catch { }
                    }
                    throw new InvalidOperationException("לא הצלחנו לבטל את האיסוף הקודם - ייתכן שהשליח כבר בדרך. נא לנסות שוב או לתאם איתו ישירות.");
                }
            }
        }
        else if (!string.IsNullOrEmpty(oldProviderId))
        {
            await _shippingService.CancelAsync(oldProviderId);
        }

        var priorReturnFee = loan.ReturnFee;
        var priorWasPaid = loan.ShippingChargeStatus is "Charged" or "PaidWithOrder";

        ShippingChargeOutcome? addedCharge = null;
        if (newMethod == ReturnMethod.CourierPickup && newReturnFee > 0 &&
            !(loan.ReturnMethod == ReturnMethod.CourierPickup && priorReturnFee > 0 && priorWasPaid))
        {
            var payer = await _userRepository.GetByIdAsync(loan.BorrowerId!)
                ?? throw new KeyNotFoundException("השוכרת לא נמצאה");
            addedCharge = await ChargeShippingAsync(payer, loan.PaymentMethod, newReturnFee, null, false, newProviderId);
        }

        foreach (var l in group)
        {
            await _loanRepository.UpdateReturnPlanAsync(l.Id!, newMethod, newTracking, newProviderId, newAddressText, l == group[0] ? newReturnFee : 0m);
            l.ReturnMethod = newMethod;
            l.ReturnTrackingId = newTracking;
            l.ReturnProviderId = newProviderId;
            l.ReturnPickupAddress = newAddressText;
            l.ReturnFee = l == group[0] ? newReturnFee : 0m;
        }

        if (addedCharge is not null)
        {
            var amount = addedCharge.Status == "Charged" ? newReturnFee : 0m;
            await _loanRepository.UpdateShippingChargeAsync(group[0].Id!, addedCharge.Status, addedCharge.PaymentId, amount);
            group[0].ShippingChargeStatus = addedCharge.Status;
            group[0].ShippingPaymentId = addedCharge.PaymentId;
            group[0].ShippingChargedAmount = amount;
        }
        else if (newMethod == ReturnMethod.SelfDropoff && priorReturnFee > 0 && priorWasPaid)
        {
            await SendRefundNeededEmailAsync(loan, priorReturnFee);
        }

        await SendReturnPlanChangedEmailsAsync(loan, item);
        loan.Item = item;
        return await MapToResponseAsync(loan);
    }

    // איך מבצעים זיכוי בסאמיט (הזיכוי נעשה רק במערכת, לא דרך ה-API)
    private const string SummitRefundStepsHtml =
        "<p><strong>איך מזכים בסאמיט:</strong> נכנסים למערכת סאמיט, פותחים את עסקת הסליקה המקורית לפי מזהה התשלום שלמעלה, " +
        "לוחצים על \"יצירת זיכוי באשראי\", בוחרים סכום (מלא או חלקי) ומאשרים. מסמך זיכוי מופק אוטומטית והכסף חוזר ללקוחה.</p>";

    private async Task SendRefundNeededEmailAsync(Loan loan, decimal amount)
    {
        if (!_emailService.IsEnabled)
            return;

        try
        {
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>נדרש זיכוי ללקוחה על ביטול איסוף החזרה בשליח.</p>" +
                       $"<p>השכרה: {System.Net.WebUtility.HtmlEncode(loan.Id)} · סכום: ₪{amount:0.##} · מזהה תשלום בסאמיט: <strong>{System.Net.WebUtility.HtmlEncode(loan.ShippingPaymentId ?? loan.PaymentTransactionId ?? "לא נמצא")}</strong>{(loan.ShippingPaymentId is null ? " (דמי האיסוף שולמו בתשלום ההזמנה)" : string.Empty)}</p>" +
                       SummitRefundStepsHtml + "</div>";
            await _emailService.SendAsync(InboxEmail, "common", "נדרש זיכוי על דמי איסוף החזרה", html);
        }
        catch
        {
        }
    }

    // מייל לשוכרת ולמשכירה על אופן ההחזרה המעודכן (כשל במייל לא מבטל את השינוי)
    private async Task SendReturnPlanChangedEmailsAsync(Loan loan, Item item)
    {
        if (!_emailService.IsEnabled)
            return;

        try
        {
            var h = (string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var borrower = !string.IsNullOrEmpty(loan.BorrowerId) ? await _userRepository.GetByIdAsync(loan.BorrowerId) : null;
            var owner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;

            if (borrower is not null && !string.IsNullOrWhiteSpace(borrower.Email))
            {
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {h(NameText.FirstName(borrower.Name))},</p>" +
                           $"<p>אופן ההחזרה של <strong>{h(LoanText.Name(item, loan))}</strong> עודכן.</p>" +
                           ReturnSummaryHtml(loan, forRenter: true) +
                           OrderButtonHtml(OrderLink(loan)) +
                           "<p>אפשר לשנות שוב בכל עת עד שהשליח יוצא לאיסוף.</p></div>";
                await _emailService.SendAsync(borrower.Email, borrower.Name, "אופן ההחזרה עודכן - common", html);
            }

            if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
            {
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                           $"<p>השוכרת עדכנה את אופן ההחזרה של <strong>{h(LoanText.Name(item, loan))}</strong>.</p>" +
                           ReturnSummaryHtml(loan, forRenter: false) +
                           OrderButtonHtml(OrderLink(loan)) + "</div>";
                await _emailService.SendAsync(owner.Email, owner.Name, "אופן ההחזרה עודכן - common", html);
            }
        }
        catch
        {
            // מייל שלא נשלח אינו סיבה לבטל את השינוי
        }
    }

    // מצב משלוח חי (זמן הגעה, שליח, מיקום) לשני הכיוונים. כשל זמני אצל חברת השילוח לא מכשיל את הבקשה
    public async Task<LoanShippingStatusDto> GetShippingStatusAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        async Task<ShipmentStatusDto?> Fetch(string? providerId, string? trackingRef)
        {
            if (string.IsNullOrEmpty(providerId))
                return null;
            try
            {
                var status = await _shippingService.GetStatusAsync(providerId);
                if (status is not null && string.IsNullOrEmpty(status.TrackingUrl) && trackingRef?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                    status.TrackingUrl = trackingRef;
                return status;
            }
            catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
            {
                return new ShipmentStatusDto
                {
                    Status = "Pending",
                    StatusLabel = "לא הצלחנו לרענן את מצב המשלוח כרגע",
                    TrackingUrl = trackingRef?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true ? trackingRef : null,
                    Note = "ננסה שוב בעוד רגע"
                };
            }
        }

        return new LoanShippingStatusDto
        {
            Outbound = await Fetch(loan.OutboundProviderId, loan.OutboundTrackingId),
            Return = await Fetch(loan.ReturnProviderId, loan.ReturnTrackingId)
        };
    }

    public async Task MarkReturnedFromLinkAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        if (loan.Status == LoanStatus.Active)
            await ReturnLoanAsync(loanId);
    }

    public async Task<NotReturnedResultDto> ReportNotReturnedAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        if (loan.Status != LoanStatus.Active || loan.NotReturnedReportedAt is not null)
            return new NotReturnedResultDto { Reported = false };

        // שליח ההחזרה כבר אסף את הפריט מהשוכרת - העיכוב אינו באשמתה, ולכן אין תזכורת ואין חיוב
        if (await CourierHasItemAsync(loan))
            return new NotReturnedResultDto { Reported = false, CourierHasItem = true };

        await _loanRepository.UpdateNotReturnedReportedAsync(loanId, DateTime.UtcNow);

        var item = !string.IsNullOrEmpty(loan.ItemCopyId) ? await _itemRepository.GetByIdAsync(loan.ItemCopyId) : null;
        var owner = !string.IsNullOrEmpty(item?.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
        var borrower = !string.IsNullOrEmpty(loan.BorrowerId) ? await _userRepository.GetByIdAsync(loan.BorrowerId) : null;

        var nowLocal = IsraelTime.Now;
        // כל יום איחור (גם חלקי) נספר כיום השכרה נוסף - לכן כשחלף מועד ההחזרה כבר נספר לפחות יום אחד
        var lateDays = IsraelTime.LateDays(loan.ExpectedReturnDate, nowLocal);
        var daily = loan.IsLocation ? 0 : loan.RateOr(item?.DailyPrice ?? 0);
        var fee = lateDays * daily * loan.Units;
        var itemName = LoanText.Name(item, loan, "הציוד");
        var due = loan.ExpectedReturnDate.ToString("dd/MM/yyyy HH:mm");
        string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
        var emailSent = false;
        var callPlaced = false;
        DateTime? callScheduledFor = null;
        var payNote = loan.PaymentMethod == PaymentMethod.CreditCard ? "ויחויב מכרטיס האשראי שלך" : "ומשולם ישירות למשכירה";

        if (borrower is not null)
        {
            // מייל לשוכרת: תזכורת להחזיר + הודעה על הקנס (יום נוסף על כל יום איחור)
            if (_emailService.IsEnabled && !string.IsNullOrWhiteSpace(borrower.Email))
            {
                try
                {
                    var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                               $"<p>שלום {H(NameText.FirstName(borrower.Name))},</p>" +
                               $"<p>המשכירה דיווחה שהציוד <strong>{H(itemName)}</strong> טרם הוחזר. ההחזרה הייתה צפויה ב-{due}.</p>" +
                               "<p><strong>אנא החזירי אותו בהקדם האפשרי.</strong></p>" +
                               "<p style=\"background:#ffe2e2;border:2px solid #c00;border-radius:10px;padding:10px\">" +
                               $"<strong>איחור מחויב:</strong> כל יום איחור (גם חלקי) נספר כיום השכרה נוסף - ₪{daily:0.##} ליום - {payNote}. " +
                               (lateDays > 0
                                   ? $"נכון לעכשיו נספרו {lateDays} ימי איחור: <strong>₪{fee:0.##}</strong>.</p>"
                                   : $"אם הציוד לא יוחזר עד {loan.ExpectedReturnDate.Add(IsraelTime.LateGrace):dd/MM/yyyy HH:mm}, יחויב יום נוסף (₪{daily:0.##}).</p>") +
                               "<p>אם הציוד כבר הוחזר או שיש בעיה - אפשר להשיב למייל הזה.</p></div>";
                    await _emailService.SendAsync(borrower.Email, borrower.Name, "הציוד טרם הוחזר - יש להחזיר בהקדם - common", html);
                    emailSent = true;
                }
                catch
                {
                    // כשל במייל לא מונע את השיחה והעדכון
                }
            }

            // שיחת טלפון לשוכרת (פעם אחת). בשבת/חג/לילה היא נדחית אוטומטית למוצאי השבת/החג או לבוקר
            if (_outboundCalls.IsEnabled && !string.IsNullOrWhiteSpace(borrower.Phone))
            {
                try
                {
                    var outcome = await _outboundCalls.CallForLoanAsync(CallKind.NotReturned, loan, item, borrower);
                    callPlaced = outcome.Placed;
                    callScheduledFor = outcome.ScheduledForLocal;
                }
                catch
                {
                    // ממשיכים
                }
            }
        }

        // עדכון תיבת האתר לטיפול הצוות
        if (_emailService.IsEnabled)
        {
            try
            {
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>המשכירה סימנה שהפריט <strong>{H(itemName)}</strong> עדיין לא הוחזר. נשלחו לשוכרת מייל ושיחת טלפון.</p>" +
                           "<table cellpadding=\"4\">" +
                           $"<tr><td>שוכרת:</td><td>{H(borrower?.Name)} · {H(borrower?.Phone)} · {H(borrower?.Email)}</td></tr>" +
                           $"<tr><td>משכירה:</td><td>{H(owner?.Name)} · {H(owner?.Phone)} · {H(owner?.Email)}</td></tr>" +
                           $"<tr><td>החזרה צפויה:</td><td>{due}</td></tr>" +
                           $"<tr><td>איחור נכון לעכשיו:</td><td>{lateDays} ימים (₪{fee:0.##})</td></tr>" +
                           "</table></div>";
                await _emailService.SendAsync(InboxEmail, "common", $"הציוד לא הוחזר: {itemName}", html);
            }
            catch
            {
                // ממשיכים
            }
        }

        return new NotReturnedResultDto
        {
            Reported = true,
            CallPlaced = callPlaced,
            CallScheduledFor = callScheduledFor,
            EmailSent = emailSent,
            LateDays = lateDays,
            DailyPrice = daily,
            FeeSoFar = fee
        };
    }

    public async Task MarkPickedUpAsync(string loanId, string? by = null)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        if (loan.Status == LoanStatus.Cancelled)
            throw new InvalidOperationException("ההשכרה בוטלה");

        if (loan.PickedUpAt is null)
            await _loanRepository.UpdatePickedUpAsync(loanId, DateTime.UtcNow, by);
    }

    public async Task<OwnerMonthSummaryDto> GetOwnerMonthSummaryAsync(string ownerId)
    {
        var items = (await _itemRepository.GetByOwnerIdAsync(ownerId)).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var now = IsraelTime.Now;
        var summary = new OwnerMonthSummaryDto();
        if (items.Count == 0)
            return summary;

        foreach (var loan in await _loanRepository.GetAllAsync())
        {
            if (loan.Status != LoanStatus.Returned || loan.ActualReturnDate is null
                || !items.TryGetValue(loan.ItemCopyId ?? string.Empty, out var item))
                continue;
            var returned = IsraelTime.FromUtc(loan.ActualReturnDate.Value);
            if (returned.Year != now.Year || returned.Month != now.Month)
                continue;
            var amount = (loan.RentalDays + loan.LateDays) * loan.RateOr(item.DailyPrice) * loan.Units;
            var rate = loan.CommissionRate ?? _commissionOptions.Percent / 100m;
            summary.Rentals++;
            summary.Earned += Math.Round(amount * (1 - rate), 2);
        }
        return summary;
    }

    public async Task<PaymentProofDto?> GetPayoutProofAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId);
        if (loan is null || string.IsNullOrEmpty(loan.OwnerPayoutProofId)) return null;
        var proof = await _paymentProofRepository.GetByIdAsync(loan.OwnerPayoutProofId);
        return proof is null ? null : new PaymentProofDto { FileName = proof.FileName, ContentType = proof.ContentType, DataUrl = proof.DataUrl };
    }

    public async Task<PaymentProofDto?> GetPaymentProofAsync(string loanId)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId);
        if (loan is null || string.IsNullOrEmpty(loan.PaymentProofId)) return null;

        var proof = await _paymentProofRepository.GetByIdAsync(loan.PaymentProofId);
        return proof is null ? null : new PaymentProofDto { FileName = proof.FileName, ContentType = proof.ContentType, DataUrl = proof.DataUrl };
    }

    public async Task<IReadOnlyList<LoanResponseDto>> GetAllAsync()
    {
        var loans = await _loanRepository.GetAllAsync();
        var result = new List<LoanResponseDto>();

        foreach (var loan in loans)
        {
            result.Add(await MapToResponseAsync(loan));
        }

        return result;
    }

    public async Task<IReadOnlyList<LoanResponseDto>> GetByBorrowerIdAsync(string borrowerId, int? limit = null)
    {
        var loans = Latest(await _loanRepository.GetByBorrowerIdAsync(borrowerId), limit);
        var result = new List<LoanResponseDto>();

        foreach (var loan in loans)
        {
            result.Add(await MapToResponseAsync(loan));
        }

        return result;
    }

    public async Task<ShippingQuoteDto> GetShippingQuoteAsync(ShippingQuoteRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.ItemId))
            throw new ArgumentException("יש לציין מזהה פריט");
        if (!IsDeliveryAddressComplete(request.DeliveryAddress))
            throw new ArgumentException("יש להזין כתובת מלאה למשלוח (עיר, רחוב ומספר בית)");

        var item = await _itemRepository.GetByIdAsync(request.ItemId)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");
        if (item.DeletedAtUtc is not null) throw new KeyNotFoundException("הפריט לא נמצא");

        var quoteOwner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
        RequireOwnerCourierDetails(quoteOwner);
        var pickupLocation = $"{item.Address}, {item.City}".Trim(',', ' ');
        var dropoff = ShippingAddressDto.FromDetails(request.DeliveryAddress!);
        var quote = await _shippingService.GetQuoteAsync(ShippingAddressDto.FromFreeText(pickupLocation), dropoff);

        quote.ReturnPrice = quote.RegularPrice;
        if (IsDeliveryAddressComplete(request.ReturnPickupAddress))
            quote.ReturnPrice = (await _shippingService.GetQuoteAsync(
                ShippingAddressDto.FromFreeText(pickupLocation), ShippingAddressDto.FromDetails(request.ReturnPickupAddress!))).RegularPrice;

        if (DateTime.TryParseExact(request.ReturnDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var returnDay)
            && DeliveryDeadline(request.ReturnTime) is { } returnBy)
        {
            var returnFrom = IsDeliveryAddressComplete(request.ReturnPickupAddress) ? ShippingAddressDto.FromDetails(request.ReturnPickupAddress!) : dropoff;
            quote.ReturnArrival = await _shippingService.CheckArrivalAsync(
                returnFrom, ShippingAddressDto.FromFreeText(pickupLocation), returnDay, returnBy, isExpress: false);
        }

        // האם המשלוח יגיע עד השעה שהשוכרת ביקשה (רגיל, ובנוסף אקספרס אם ההשכרה מתחילה היום)
        var deadline = DeliveryDeadline(request.StartTime);
        if (deadline is { } by
            && DateTime.TryParseExact(request.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            quote.RegularArrival = await _shippingService.CheckArrivalAsync(ShippingAddressDto.FromFreeText(pickupLocation), dropoff, day, by, isExpress: false);
            if (day.Date == IsraelTime.Now.Date && quote.ExpressPrice > 0)
                quote.ExpressArrival = await _shippingService.CheckArrivalAsync(ShippingAddressDto.FromFreeText(pickupLocation), dropoff, day, by, isExpress: true);
            else if (day.Date > IsraelTime.Now.Date && quote.ExpressPrice > 0 && quote.RegularArrival is { Possible: false })
                quote.ExpressArrival = new ArrivalCheckDto
                {
                    Possible = true,
                    Verified = true,
                    Message = $"משלוח אקספרס: השליח יוזמן ביום ההשכרה ויגיע עד {by:hh\\:mm}"
                };
        }

        return quote;
    }

    // בודק שכתובת המסירה מכילה לפחות עיר/רחוב/מספר בית - קומה אופציונלית (ראו DeliveryAddressDto)
    // כתובת האיסוף העצמי כפי שמוצגת לשוכרת: הרחוב והמספר, וגם הקומה והדירה/שם המשפחה כדי שתמצא את הדלת
    internal static string? PickupAddressLine(string? address, User? owner)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address)) parts.Add(address.Trim());
        if (!string.IsNullOrWhiteSpace(owner?.AddressFloor)) parts.Add($"קומה {owner.AddressFloor.Trim()}");
        if (!string.IsNullOrWhiteSpace(owner?.AddressApartment)) parts.Add($"דירה/משפחה {owner.AddressApartment.Trim()}");
        return parts.Count == 0 ? address : string.Join(", ", parts);
    }

    public Task<(string ProofId, EmailAttachment Attachment)> StoreSaleProofAsync(string? dataUrl, string? fileName) =>
        StoreProofAsync(dataUrl, fileName);

    public async Task<decimal> GetSaleDeliveryFeeAsync(Item item, DeliveryAddressDto address)
    {
        if (!IsCourierAddressComplete(address))
            throw new ArgumentException("יש להזין כתובת מלאה למשלוח: עיר, רחוב, מספר בית, קומה ומספר דירה או שם משפחה");
        var owner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
        RequireOwnerCourierDetails(owner);
        var pickupLocation = $"{item.Address}, {item.City}".Trim(',', ' ');
        var quote = await _shippingService.GetQuoteAsync(ShippingAddressDto.FromFreeText(pickupLocation), ShippingAddressDto.FromDetails(address));
        return quote.RegularPrice;
    }

    public async Task<(string? TrackingId, string? ProviderId, string AddressText)> DispatchSaleAsync(Item item, User buyer, DeliveryAddressDto address)
    {
        var owner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
        RequireOwnerCourierDetails(owner);
        var ownerAddress = OwnerShippingAddress($"{item.Address}, {item.City}".Trim(',', ' '), owner);
        var day = IsraelTime.Now.Hour >= 14 ? IsraelTime.Now.Date.AddDays(1) : IsraelTime.Now.Date;
        while (day.DayOfWeek is DayOfWeek.Saturday || (day.DayOfWeek is DayOfWeek.Friday && day == IsraelTime.Now.Date && IsraelTime.Now.Hour >= 11))
            day = day.AddDays(1);
        var shipment = await _shippingService.RequestPickupAsync(ownerAddress.WithContact(owner?.Name, owner?.Phone),
            ShippingAddressDto.FromDetails(address).WithContact(buyer.Name, buyer.Phone), day, isExpress: false);
        return (shipment.TrackingRef, shipment.ProviderId, FormatDeliveryAddress(address));
    }

    private static ShippingAddressDto OwnerShippingAddress(string freeText, User? owner)
    {
        var address = ShippingAddressDto.FromFreeText(freeText, owner?.AddressNotes);
        address.Floor = owner?.AddressFloor;
        address.Apartment = owner?.AddressApartment;
        return address;
    }

    private static void RequireOwnerCourierDetails(User? owner)
    {
        if (owner is null || string.IsNullOrWhiteSpace(owner.AddressFloor) || string.IsNullOrWhiteSpace(owner.AddressApartment))
            throw new InvalidOperationException("אצל המשכירה חסרים פרטי כתובת (קומה ומספר דירה או שם משפחה), ולכן אי אפשר כרגע להזמין משלוח או איסוף שליח. אפשר לבחור איסוף עצמי.");
    }

    private static bool IsCourierAddressComplete(DeliveryAddressDto? address) =>
        IsDeliveryAddressComplete(address) &&
        !string.IsNullOrWhiteSpace(address!.Floor) &&
        !string.IsNullOrWhiteSpace(address.Apartment);

    private static bool IsDeliveryAddressComplete(DeliveryAddressDto? address) =>
        address is not null &&
        !string.IsNullOrWhiteSpace(address.City) &&
        !string.IsNullOrWhiteSpace(address.Street) &&
        !string.IsNullOrWhiteSpace(address.HouseNumber);

    // מרכיב מחרוזת כתובת קריאה לתצוגה/שמירה (ב-Loan.DeliveryAddress, אימיילים וכו') מתוך הכתובת
    // המפורטת שמתקבלת מהלקוח - "מקור האמת" לצורך חיוב ודיספאץ' בפועל הוא תמיד DeliveryAddressDto
    // המובנה (ראו GetPackageShippingService), לא המחרוזת המורכבת הזו
    private static string FormatDeliveryAddress(DeliveryAddressDto address)
    {
        var line = $"{address.Street} {address.HouseNumber}".Trim();
        if (!string.IsNullOrWhiteSpace(address.Floor))
            line += $", קומה {address.Floor}";
        if (!string.IsNullOrWhiteSpace(address.Apartment))
            line += $", דירה/משפחה {address.Apartment}";
        line += $", {address.City}";
        return line;
    }

    // שמירת ההשכרות מול ה-concurrency token של הפריטים. בדיקת הזמינות (EnsureFreeAsync) התבססה על
    // הגרסה שנקראה - אם הפריט הושכר בינתיים, השמירה נדחית ומוחזר 409
    private async Task<IReadOnlyList<Loan>> SaveBookingsAsync(IReadOnlyList<(Loan Loan, Item Item)> bookings, CancellationToken ct)
    {
        try
        {
            return await _loanRepository.CreateWithItemCheckAsync(bookings, ct);
        }
        catch (ConcurrencyConflictException ex)
        {
            throw new ConcurrencyConflictException("הפריט נתפס על ידי משתמשת אחרת ממש עכשיו. נסי שוב או בחרי תאריכים אחרים.", ex.ResourceId, ex);
        }
    }

    public async Task<LoanResponseDto> CreateAsync(LoanRequestDto request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ItemId))
            throw new ArgumentException("יש לציין מזהה פריט (ItemId)");
        if (string.IsNullOrWhiteSpace(request.BorrowerId))
            throw new ArgumentException("יש לציין מזהה שוכר (BorrowerId)");

        var item = await _itemRepository.GetByIdAsync(request.ItemId, ct)
            ?? throw new KeyNotFoundException("הפריט המבוקש לא נמצא");
        if (item.DeletedAtUtc is not null) throw new KeyNotFoundException("הפריט המבוקש לא נמצא");

        var borrower = await _userRepository.GetByIdAsync(request.BorrowerId, ct)
            ?? throw new KeyNotFoundException("השוכר המבוקש לא נמצא");
        RequireOrderPrerequisites(request.TermsAccepted, request.PhoneConfirmed, borrower);

        var deliveryMethod = ParseDeliveryMethod(request.DeliveryMethod);

        // תאריך התחלה: ברירת המחדל היא היום. מספר הימים חייב לכבד את משך ההשכרה המינימלי שקבע
        // המשכיר (אם הגדיר יחידת מינימום של ימים) - יחידת שעות אינה נאכפת כאן כרגע כדי לשמור על
        // מודל פשוט של "ימי השכרה" בעסקה עצמה.
        var today = IsraelTime.Now.Date;
        var startDate = request.StartDate == default ? today : request.StartDate.Date;

        if (startDate < today)
            throw new ArgumentException("לא ניתן לבחור תאריך התחלה שכבר עבר");
        EnsureStartNotPast(startDate, request.StartTime);

        // סטודיו/לוקיישן: ביום אחד לפי שעות, הגעה עצמית - בלי משלוח ובלי איסוף החזרה
        if (item.IsLocation)
        {
            request.SameDayReturn = true;
            request.RentalDays = 1;
            request.Quantity = 1;
            request.ExpressDelivery = false;
            if (request.DeliveryMethod != "SelfPickup" || request.ReturnMethod != "SelfDropoff")
                throw new ArgumentException("בסטודיו ולוקיישן ההגעה היא עצמית - בלי משלוח");
        }

        var rentalDays = request.RentalDays > 0 ? request.RentalDays : 1;

        if (item.MinRentalUnit == RentalUnit.Days && rentalDays < item.MinRentalAmount)
            throw new ArgumentException($"משך ההשכרה המינימלי לפריט זה הוא {item.MinRentalAmount} ימים");

        // משלוח אקספרס רלוונטי רק כאשר ההזמנה היא למשלוח והתחלת ההשכרה היא היום - בכל מקרה אחר מתעלמים מהבקשה
        var expressDelivery = request.ExpressDelivery && deliveryMethod == DeliveryMethod.Delivery && startDate >= today;

        // ===== אופן ומועד ההחזרה - נקבעים מראש יחד עם פתיחת ההשכרה =====
        var returnMethod = ParseReturnMethod(request.ReturnMethod);

        var returnTimeOfDay = ParseReturnTime(request.ReturnTime);
        var expectedReturnDate = SameDayReturnDate(request.SameDayReturn, startDate, rentalDays, request.StartTime, returnTimeOfDay);
        var chargedDays = request.SameDayReturn ? rentalDays : ShabbatPricing.ChargedDays(startDate, rentalDays);
        var quantity = Math.Max(1, request.Quantity);
        var rate = item.IsLocation ? LocationPrice(item, RequestedStart(startDate, request.StartTime), expectedReturnDate) : item.DailyPrice;
        await _schedule.EnsureFreeAsync(item, RequestedStart(startDate, request.StartTime), expectedReturnDate, quantity);

        // כתובת השוכר נדרשת כאשר יש בפועל מעורבות שליח בהובלה - במשלוח יוצא ו/או באיסוף שליח בהחזרה
        var needsDeliveryAddress = deliveryMethod == DeliveryMethod.Delivery || returnMethod == ReturnMethod.CourierPickup;
        if (needsDeliveryAddress && !IsCourierAddressComplete(request.DeliveryAddress))
            throw new ArgumentException("יש להזין כתובת מלאה למשלוח/איסוף שליח: עיר, רחוב, מספר בית, קומה ומספר דירה או שם משפחה");

        var ownerLocation = $"{item.Address}, {item.City}".Trim(',', ' ');
        // הערות הכתובת של המשכירה (אם יש) נשלחות לשליח - נטענות רק כשיש מעורבות שליח
        var ownerForNotes = needsDeliveryAddress && !string.IsNullOrEmpty(item.OwnerId)
            ? await _userRepository.GetByIdAsync(item.OwnerId, ct)
            : null;
        if (needsDeliveryAddress) RequireOwnerCourierDetails(ownerForNotes);
        var ownerAddress = OwnerShippingAddress(ownerLocation, ownerForNotes);
        var borrowerAddress = needsDeliveryAddress ? ShippingAddressDto.FromDetails(request.DeliveryAddress!) : null;
        var (returnPickupAddress, returnPickupText) =
            ResolveReturnPickup(deliveryMethod, returnMethod, request.ReturnPickupAddress, borrowerAddress);

        // ===== עלות משלוח - הצעת מחיר אמיתית מחברת השילוח (זהה למחיר שכבר הוצג ללקוח לפני
        // אימות הזמינות דרך GetShippingQuoteAsync), ולא סכום קבוע מנוחש =====
        var deliveryFee = 0m;
        if (deliveryMethod == DeliveryMethod.Delivery)
        {
            var quote = await _shippingService.GetQuoteAsync(ownerAddress, borrowerAddress!);
            if (expressDelivery && quote.ExpressPrice <= 0)
                throw new ArgumentException("משלוח אקספרס אינו זמין כרגע - נא לבחור משלוח רגיל");
            deliveryFee = expressDelivery ? quote.ExpressPrice : quote.RegularPrice;
        }

        var returnFee = returnMethod == ReturnMethod.CourierPickup
            ? (await _shippingService.GetQuoteAsync(ownerAddress, returnPickupAddress!)).RegularPrice
            : 0m;

        // ===== אמצעי תשלום =====
        var paymentMethod = ParsePaymentMethod(request.PaymentMethod);

        if (paymentMethod == PaymentMethod.Cash && deliveryMethod != DeliveryMethod.SelfPickup)
            throw new ArgumentException("תשלום במזומן אפשרי רק באיסוף עצמי - יש לבחור אמצעי תשלום אחר");

        User? owner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId, ct) : null;
        var paymentStatus = PaymentStatus.Pending;
        string? paymentTransactionId = null;
        string? paymentProofId = null;
        EmailAttachment? proofAttachment = null;

        if (paymentMethod == PaymentMethod.BankTransfer)
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.PayoutAccountNumber))
                throw new InvalidOperationException("המשכיר טרם הגדיר פרטי חשבון לקבלת תשלומים - לא ניתן לשלם בהעברה בנקאית עבור פריט זה");

            (paymentProofId, proofAttachment) = await StoreProofAsync(request.PaymentProofDataUrl, request.PaymentProofFileName);
        }
        else if (paymentMethod == PaymentMethod.CreditCard)
        {
            await _orderPayments.VerifyAsync(borrower, chargedDays * rate * quantity + deliveryFee + returnFee, request.PaymentTransactionId);

            paymentTransactionId = request.PaymentTransactionId;
            paymentStatus = PaymentStatus.Paid;
        }

        var loan = new Loan
        {
            ItemCopyId = item.Id,
            BorrowerId = borrower.Id,
            LoanDate = DateTime.UtcNow,
            StartDate = startDate,
            RentalDays = chargedDays,
            Quantity = quantity,
            DailyRate = rate,
            IsLocation = item.IsLocation,
            StartTime = NormalizeTimeOfDay(request.StartTime),
            CommissionRate = _commissionOptions.Percent / 100m,
            ExpectedReturnDate = expectedReturnDate,
            Status = LoanStatus.Active,
            DeliveryMethod = deliveryMethod,
            ExpressDelivery = expressDelivery,
            DeliveryFee = deliveryFee,
            ReturnFee = returnFee,
            ConfirmedPhone = borrower.Phone,
            DeliveryAddress = needsDeliveryAddress ? FormatDeliveryAddress(request.DeliveryAddress!) : null,
            ReturnPickupAddress = returnPickupText,
            ReturnMethod = returnMethod,
            PaymentMethod = paymentMethod,
            PaymentStatus = paymentStatus,
            PaymentTransactionId = paymentTransactionId,
            PaymentProofId = paymentProofId,
            OwnerActionToken = NewActionToken(),
            TermsAcceptedAtUtc = DateTime.UtcNow,
            TermsVersion = TermsInfo.CurrentVersion,
            PhoneConfirmedAtUtc = DateTime.UtcNow
        };

        // דיספאץ' אוטומטי לחברת השילוח (מדומה כרגע - ראו SimulatedShippingService) - כבר עכשיו,
        // בעת פתיחת ההשכרה, ולא ממתינים למועד בפועל
        if (deliveryMethod == DeliveryMethod.Delivery && expressDelivery && startDate > today)
        {
            loan.ExpressDispatchAtLocal = ExpressDispatchTime(startDate, request.StartTime);
        }
        else if (deliveryMethod == DeliveryMethod.Delivery)
        {
            var outbound = await _shippingService.RequestPickupAsync(ownerAddress.WithContact(owner?.Name, owner?.Phone), borrowerAddress!.WithContact(borrower.Name, borrower.Phone), startDate, expressDelivery, DeliveryDeadline(request.StartTime));
            loan.OutboundTrackingId = outbound.TrackingRef;
            loan.OutboundProviderId = outbound.ProviderId;
        }

        // להחזרה אין אפשרות "אקספרס" באתר - תמיד נשלחת כמסלול משותף (הזול/הרגיל)
        if (returnMethod == ReturnMethod.CourierPickup)
        {
            var returnPickup = await _shippingService.RequestPickupAsync(returnPickupAddress!.WithContact(borrower.Name, borrower.Phone), ownerAddress.WithContact(owner?.Name, owner?.Phone), expectedReturnDate, isExpress: false, expectedReturnDate.TimeOfDay);
            loan.ReturnTrackingId = returnPickup.TrackingRef;
            loan.ReturnProviderId = returnPickup.ProviderId;
        }

        var shippingFee = deliveryFee + returnFee;
        var shippingCharge = await ChargeShippingAsync(
            borrower, paymentMethod, shippingFee, request.ShippingPaymentId, true, loan.OutboundProviderId, loan.ReturnProviderId);
        loan.ShippingChargeStatus = shippingCharge.Status;
        loan.ShippingPaymentId = shippingCharge.PaymentId;
        loan.ShippingChargedAmount = shippingCharge.Status == "Charged" ? shippingFee : 0m;

        var created = (await SaveBookingsAsync(new[] { (loan, item) }, ct))[0];

        // סטטוס הפריט: "מושכר" רק כשההשכרה כבר התחילה; הזמנה עתידית לא חוסמת השכרות בתאריכים אחרים
        await _schedule.RefreshStatusAsync(item.Id!, item.RentalCount is { } rentals ? rentals + 1 : null);
        item.Status = RequestedStart(startDate, request.StartTime) <= IsraelTime.Now ? CopyStatus.Rented : item.Status;

        created.Item = item;
        created.Item.Owner = owner;

        if (created.PaymentStatus == PaymentStatus.Paid)
        {
            await SendPaymentReceivedEmailAsync(created, owner);
        }

        await SendRentalConfirmationEmailsAsync(new[] { created }, owner, borrower, proofAttachment);

        return MapToResponse(created, owner);
    }

    public async Task<IReadOnlyList<LoanResponseDto>> CreateBatchAsync(LoanBatchRequestDto request, CancellationToken ct = default)
    {
        var itemIds = request.ItemIds.Distinct().ToList();

        if (itemIds.Count == 0)
            throw new ArgumentException("יש לבחור לפחות פריט אחד להזמנה");
        if (string.IsNullOrWhiteSpace(request.BorrowerId))
            throw new ArgumentException("יש לציין מזהה שוכר (BorrowerId)");

        var items = new List<Item>();
        var loaded = await _itemRepository.GetByIdsAsync(itemIds, ct);
        foreach (var itemId in itemIds)
        {
            var item = loaded.GetValueOrDefault(itemId)
                ?? throw new KeyNotFoundException("אחד הפריטים בהזמנה לא נמצא");
            if (item.DeletedAtUtc is not null) throw new KeyNotFoundException("אחד הפריטים בהזמנה לא נמצא");

            items.Add(item);
        }

        if (items.Any(i => i.IsLocation))
            throw new ArgumentException("סטודיו ולוקיישן מזמינים בנפרד, לא יחד עם ציוד");

        if (items.Select(i => i.OwnerId).Distinct().Count() > 1)
            throw new ArgumentException("כל הפריטים בהזמנה אחת חייבים להיות מאותה צלמת בקהילה");

        var borrower = await _userRepository.GetByIdAsync(request.BorrowerId, ct)
            ?? throw new KeyNotFoundException("השוכר המבוקש לא נמצא");
        RequireOrderPrerequisites(request.TermsAccepted, request.PhoneConfirmed, borrower);

        var deliveryMethod = ParseDeliveryMethod(request.DeliveryMethod);

        var today = IsraelTime.Now.Date;
        var startDate = request.StartDate == default ? today : request.StartDate.Date;

        if (startDate < today)
            throw new ArgumentException("לא ניתן לבחור תאריך התחלה שכבר עבר");
        EnsureStartNotPast(startDate, request.StartTime);

        var rentalDays = request.RentalDays > 0 ? request.RentalDays : 1;

        foreach (var item in items)
        {
            if (item.MinRentalUnit == RentalUnit.Days && rentalDays < item.MinRentalAmount)
                throw new ArgumentException($"משך ההשכרה המינימלי לפריט \"{item.Name}\" הוא {item.MinRentalAmount} ימים");
        }

        var expressDelivery = request.ExpressDelivery && deliveryMethod == DeliveryMethod.Delivery && startDate >= today;

        var returnMethod = ParseReturnMethod(request.ReturnMethod);
        var expectedReturnDate = SameDayReturnDate(request.SameDayReturn, startDate, rentalDays, request.StartTime, ParseReturnTime(request.ReturnTime));
        var chargedDays = request.SameDayReturn ? rentalDays : ShabbatPricing.ChargedDays(startDate, rentalDays);
        int UnitsOf(Item i) => Math.Max(1, request.Quantities?.GetValueOrDefault(i.Id!) ?? 1);
        foreach (var item in items)
            await _schedule.EnsureFreeAsync(item, RequestedStart(startDate, request.StartTime), expectedReturnDate, UnitsOf(item));

        var needsDeliveryAddress = deliveryMethod == DeliveryMethod.Delivery || returnMethod == ReturnMethod.CourierPickup;
        if (needsDeliveryAddress && !IsCourierAddressComplete(request.DeliveryAddress))
            throw new ArgumentException("יש להזין כתובת מלאה למשלוח/איסוף שליח: עיר, רחוב, מספר בית, קומה ומספר דירה או שם משפחה");

        var batchOwnerLocation = $"{items[0].Address}, {items[0].City}".Trim(',', ' ');
        var batchOwnerForNotes = needsDeliveryAddress && !string.IsNullOrEmpty(items[0].OwnerId)
            ? await _userRepository.GetByIdAsync(items[0].OwnerId)
            : null;
        if (needsDeliveryAddress) RequireOwnerCourierDetails(batchOwnerForNotes);
        var batchOwnerAddress = OwnerShippingAddress(batchOwnerLocation, batchOwnerForNotes);
        var batchBorrowerAddress = needsDeliveryAddress ? ShippingAddressDto.FromDetails(request.DeliveryAddress!) : null;
        var (batchReturnPickupAddress, batchReturnPickupText) =
            ResolveReturnPickup(deliveryMethod, returnMethod, request.ReturnPickupAddress, batchBorrowerAddress);

        // עלות המשלוח נגבית פעם אחת בלבד עבור ההזמנה כולה (משלוח יוצא בודד לכל הפריטים יחד) - לא כפול
        // פריט. הצעת מחיר אמיתית מחברת השילוח (זהה למחיר שכבר הוצג ללקוח לפני אימות הזמינות) - לא סכום קבוע
        var deliveryFee = 0m;
        if (deliveryMethod == DeliveryMethod.Delivery)
        {
            var quote = await _shippingService.GetQuoteAsync(batchOwnerAddress, batchBorrowerAddress!);
            if (expressDelivery && quote.ExpressPrice <= 0)
                throw new ArgumentException("משלוח אקספרס אינו זמין כרגע - נא לבחור משלוח רגיל");
            deliveryFee = expressDelivery ? quote.ExpressPrice : quote.RegularPrice;
        }

        var returnFee = returnMethod == ReturnMethod.CourierPickup
            ? (await _shippingService.GetQuoteAsync(batchOwnerAddress, batchReturnPickupAddress!)).RegularPrice
            : 0m;

        var paymentMethod = ParsePaymentMethod(request.PaymentMethod);

        if (paymentMethod == PaymentMethod.Cash && deliveryMethod != DeliveryMethod.SelfPickup)
            throw new ArgumentException("תשלום במזומן אפשרי רק באיסוף עצמי - יש לבחור אמצעי תשלום אחר");

        var ownerId = items[0].OwnerId;
        User? owner = !string.IsNullOrEmpty(ownerId) ? await _userRepository.GetByIdAsync(ownerId) : null;
        var paymentStatus = PaymentStatus.Pending;
        string? paymentTransactionId = null;
        string? paymentProofId = null;
        EmailAttachment? proofAttachment = null;

        if (paymentMethod == PaymentMethod.BankTransfer)
        {
            if (owner is null || string.IsNullOrWhiteSpace(owner.PayoutAccountNumber))
                throw new InvalidOperationException("המשכיר טרם הגדיר פרטי חשבון לקבלת תשלומים - לא ניתן לשלם בהעברה בנקאית עבור הזמנה זו");

            (paymentProofId, proofAttachment) = await StoreProofAsync(request.PaymentProofDataUrl, request.PaymentProofFileName);
        }
        else if (paymentMethod == PaymentMethod.CreditCard)
        {
            await _orderPayments.VerifyAsync(borrower, items.Sum(i => chargedDays * i.DailyPrice * UnitsOf(i)) + deliveryFee + returnFee, request.PaymentTransactionId);

            paymentTransactionId = request.PaymentTransactionId;
            paymentStatus = PaymentStatus.Paid;
        }

        // דיספאץ' אוטומטי לחברת השילוח - פעם אחת בלבד עבור ההזמנה כולה (לא לכל פריט בנפרד)
        var delayedExpress = deliveryMethod == DeliveryMethod.Delivery && expressDelivery && startDate > today;
        var outboundShipment = deliveryMethod == DeliveryMethod.Delivery && !delayedExpress
            ? await _shippingService.RequestPickupAsync(batchOwnerAddress.WithContact(owner?.Name, owner?.Phone), batchBorrowerAddress!.WithContact(borrower.Name, borrower.Phone), startDate, expressDelivery, DeliveryDeadline(request.StartTime))
            : null;
        // להחזרה אין אפשרות "אקספרס" באתר - תמיד נשלחת כמסלול משותף (הזול/הרגיל)
        var returnShipment = returnMethod == ReturnMethod.CourierPickup
            ? await _shippingService.RequestPickupAsync(batchReturnPickupAddress!.WithContact(borrower.Name, borrower.Phone), batchOwnerAddress.WithContact(owner?.Name, owner?.Phone), expectedReturnDate, isExpress: false, expectedReturnDate.TimeOfDay)
            : null;

        var shippingFee = deliveryFee + returnFee;
        var shippingCharge = await ChargeShippingAsync(
            borrower, paymentMethod, shippingFee, request.ShippingPaymentId, true, outboundShipment?.ProviderId, returnShipment?.ProviderId);

        var orderGroupId = Guid.NewGuid().ToString("N");
        var createdLoans = new List<Loan>();
        var pending = new List<(Loan Loan, Item Item)>();

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var loan = new Loan
            {
                ItemCopyId = item.Id,
                BorrowerId = borrower.Id,
                LoanDate = DateTime.UtcNow,
                StartDate = startDate,
                RentalDays = chargedDays,
                Quantity = UnitsOf(item),
                DailyRate = item.DailyPrice,
                StartTime = NormalizeTimeOfDay(request.StartTime),
                CommissionRate = _commissionOptions.Percent / 100m,
                ExpectedReturnDate = expectedReturnDate,
                Status = LoanStatus.Active,
                DeliveryMethod = deliveryMethod,
                ExpressDelivery = expressDelivery,
                ExpressDispatchAtLocal = delayedExpress ? ExpressDispatchTime(startDate, request.StartTime) : null,
                // עלות המשלוח נזקפת רק לפריט הראשון בהזמנה, כדי שהסכום הכולל לא ייספר פעמיים
                DeliveryFee = i == 0 ? deliveryFee : 0m,
                ReturnFee = i == 0 ? returnFee : 0m,
                ShippingChargeStatus = i == 0 ? shippingCharge.Status : null,
                ShippingPaymentId = i == 0 ? shippingCharge.PaymentId : null,
                ShippingChargedAmount = i == 0 && shippingCharge.Status == "Charged" ? shippingFee : 0m,
                ConfirmedPhone = borrower.Phone,
                DeliveryAddress = needsDeliveryAddress ? FormatDeliveryAddress(request.DeliveryAddress!) : null,
            ReturnPickupAddress = batchReturnPickupText,
                ReturnMethod = returnMethod,
                PaymentMethod = paymentMethod,
                PaymentStatus = paymentStatus,
                PaymentTransactionId = paymentTransactionId,
                PaymentProofId = paymentProofId,
                OwnerActionToken = NewActionToken(),
                TermsAcceptedAtUtc = DateTime.UtcNow,
                TermsVersion = TermsInfo.CurrentVersion,
                PhoneConfirmedAtUtc = DateTime.UtcNow,
                OrderGroupId = orderGroupId,
                OutboundTrackingId = outboundShipment?.TrackingRef,
                ReturnTrackingId = returnShipment?.TrackingRef,
                OutboundProviderId = outboundShipment?.ProviderId,
                ReturnProviderId = returnShipment?.ProviderId
            };

            pending.Add((loan, item));
        }

        await SaveBookingsAsync(pending, ct);

        foreach (var (created, item) in pending)
        {
            await _schedule.RefreshStatusAsync(item.Id!, item.RentalCount is { } rentals ? rentals + 1 : null);
            item.Status = RequestedStart(startDate, request.StartTime) <= IsraelTime.Now ? CopyStatus.Rented : item.Status;

            created.Item = item;
            created.Item.Owner = owner;
            createdLoans.Add(created);
        }

        if (paymentStatus == PaymentStatus.Paid)
        {
            await SendOrderPaymentReceivedEmailAsync(createdLoans, owner);
        }

        await SendRentalConfirmationEmailsAsync(createdLoans, owner, borrower, proofAttachment);

        return createdLoans.Select(loan => MapToResponse(loan, owner)).ToList();
    }

    private static DateTime StartMoment(Loan loan)
    {
        var time = NormalizeTimeOfDay(loan.StartTime);
        var start = loan.StartDate.Date + (time is null ? TimeSpan.Zero : TimeSpan.Parse(time, CultureInfo.InvariantCulture));
        return DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
    }

    private static DateTime? CancelDeadlineOf(Loan loan) =>
        loan.Status == LoanStatus.Active && loan.PickedUpAt is null ? StartMoment(loan).AddHours(-24) : null;

    public async Task<CancelLoanResultDto> CancelLoanAsync(string loanId, string actor, bool isAdmin)
    {
        var loan = await _loanRepository.GetByIdAsync(loanId)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        if (loan.Status != LoanStatus.Active || loan.PickedUpAt is not null)
            throw new InvalidOperationException("אי אפשר לבטל השכרה שכבר החלה או הסתיימה");

        var deadline = StartMoment(loan).AddHours(-24);
        if (!isAdmin && IsraelTime.Now > deadline)
            throw new InvalidOperationException($"אפשר לבטל עד 24 שעות לפני תחילת ההשכרה (עד {deadline:dd/MM/yyyy HH:mm}). אפשר לפנות להנהלת האתר בדף צור קשר");

        var group = string.IsNullOrEmpty(loan.OrderGroupId)
            ? new List<Loan> { loan }
            : (await _loanRepository.GetAllAsync())
                .Where(l => l.OrderGroupId == loan.OrderGroupId && l.Status == LoanStatus.Active && l.PickedUpAt is null)
                .ToList();

        var now = DateTime.UtcNow;
        var providerIds = new HashSet<string>();
        foreach (var l in group)
        {
            await _loanRepository.UpdateStatusAsync(l.Id!, LoanStatus.Cancelled);
            await _loanRepository.UpdateCancellationAsync(l.Id!, now, actor);
            l.Status = LoanStatus.Cancelled;

            if (!string.IsNullOrEmpty(l.OutboundProviderId)) providerIds.Add(l.OutboundProviderId);
            if (!string.IsNullOrEmpty(l.ReturnProviderId)) providerIds.Add(l.ReturnProviderId);

            if (!string.IsNullOrEmpty(l.ItemCopyId))
                await _schedule.RefreshStatusAsync(l.ItemCopyId);
        }

        var shipmentFailures = 0;
        foreach (var providerId in providerIds)
        {
            try { await _shippingService.CancelAsync(providerId); }
            catch { shipmentFailures++; }
        }

        var first = group[0];
        var item = !string.IsNullOrEmpty(first.ItemCopyId) ? await _itemRepository.GetByIdAsync(first.ItemCopyId) : null;
        var result = await SendCancellationEmailsAsync(group, item, actor, shipmentFailures);
        return new CancelLoanResultDto { CancelledCount = group.Count, RefundNote = result };
    }

    // מיילים על הביטול: לשוכרת, למשכירה, ולהנהלה כשצריך לבצע זיכוי ידני בסאמיט. מחזיר טקסט לתצוגה
    private async Task<string> SendCancellationEmailsAsync(IReadOnlyList<Loan> loans, Item? item, string actor, int shipmentFailures)
    {
        var first = loans[0];
        var names = string.Join(", ", loans.Select(l => LoanText.Name(l.Item ?? item, l)));
        var byText = actor switch { "Owner" => "המשכירה", "Admin" => "הנהלת האתר", _ => "השוכרת" };

        var cardTotal = first.PaymentMethod == PaymentMethod.CreditCard && first.PaymentStatus == PaymentStatus.Paid
            ? loans.Sum(l => l.RentalDays * l.RateOr(l.Item?.DailyPrice ?? item?.DailyPrice ?? 0) * l.Units + l.DeliveryFee + l.ReturnFee)
            : 0m;
        var shippingRefund = loans.Where(l => l.ShippingChargeStatus == "Charged").Sum(l => l.ShippingChargedAmount);
        var directPayment = first.PaymentMethod is PaymentMethod.BankTransfer;

        string refundNote;
        if (cardTotal > 0 || shippingRefund > 0)
            refundNote = "הכסף יוחזר אלייך לכרטיס האשראי בימים הקרובים.";
        else if (directPayment)
            refundNote = "התשלום הועבר ישירות למשכירה - אם הועבר כסף, המשכירה תחזיר אותו אלייך.";
        else
            refundNote = "לא נגבה ממך כסף, ואין צורך בהחזר.";

        if (!_emailService.IsEnabled)
            return refundNote;

        var h = (string? t) => System.Net.WebUtility.HtmlEncode(t ?? string.Empty);
        try
        {
            var borrower = !string.IsNullOrEmpty(first.BorrowerId) ? await _userRepository.GetByIdAsync(first.BorrowerId) : null;
            var owner = !string.IsNullOrEmpty(item?.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
            var start = StartMoment(first).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

            if (borrower is not null && !string.IsNullOrWhiteSpace(borrower.Email))
            {
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {h(NameText.FirstName(borrower.Name))},</p>" +
                           (actor is "Owner" or "Admin"
                               ? $"<p>ההשכרה של <strong>{h(names)}</strong> שהייתה אמורה להתחיל ב-{start} בוטלה על ידי {byText}.</p>"
                               : $"<p>ההשכרה של <strong>{h(names)}</strong> שהייתה אמורה להתחיל ב-{start} בוטלה לבקשתך.</p>") +
                           $"<p>{h(refundNote)}</p></div>";
                await _emailService.SendAsync(borrower.Email, borrower.Name, "ההשכרה בוטלה - common", html);
            }

            if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
            {
                var directNote = directPayment ? "<p>אם קיבלת העברה בנקאית מהשוכרת - יש להחזיר לה את הכסף.</p>" : string.Empty;
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                           $"<p>ההשכרה של <strong>{h(names)}</strong> שהייתה אמורה להתחיל ב-{start} בוטלה על ידי {byText}. הפריט חזר להיות זמין להשכרה.</p>" +
                           directNote + "</div>";
                await _emailService.SendAsync(owner.Email, owner.Name, "ההשכרה בוטלה - common", html);
            }

            if (cardTotal > 0 || shippingRefund > 0 || shipmentFailures > 0)
            {
                var lines = new List<string>();
                if (cardTotal > 0) lines.Add($"זיכוי על התשלום באשראי: ₪{cardTotal:0.##} - מזהה תשלום בסאמיט: <strong>{h(first.PaymentTransactionId)}</strong>");
                if (shippingRefund > 0) lines.Add($"זיכוי על חיוב דמי משלוח: ₪{shippingRefund:0.##} - מזהה תשלום בסאמיט: <strong>{h(first.ShippingPaymentId)}</strong>");
                if (shipmentFailures > 0) lines.Add($"ביטול שליחים אצל GetPackage נכשל ב-{shipmentFailures} מקרים - יש לבדוק ולבטל ידנית");
                var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                           $"<p>השכרה בוטלה על ידי {byText}: {h(names)} (מזהה {h(first.Id)}).</p>" +
                           $"<ul>{string.Join("", lines.Select(x => $"<li>{x}</li>"))}</ul>" +
                           ((cardTotal > 0 || shippingRefund > 0) ? SummitRefundStepsHtml : string.Empty) + "</div>";
                await _emailService.SendAsync(InboxEmail, "common", "ביטול השכרה - נדרש טיפול", html);
            }
        }
        catch
        {
        }

        return refundNote;
    }

    // האם שליח ההחזרה כבר אסף את הפריט מהשוכרת (סטטוס "נאסף" או "נמסר" אצל חברת השילוח). אם אי אפשר לאמת - לא
    private async Task<bool> CourierHasItemAsync(Loan loan)
    {
        if (loan.ReturnMethod != ReturnMethod.CourierPickup || string.IsNullOrEmpty(loan.ReturnProviderId))
            return false;

        try
        {
            var status = await _shippingService.GetStatusAsync(loan.ReturnProviderId);
            return status?.Status is "PickedUp" or "Delivered";
        }
        catch
        {
            return false;
        }
    }

    public async Task ReturnLoanAsync(string id)
    {
        var loan = await _loanRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        if (loan.Status == LoanStatus.Cancelled)
            throw new InvalidOperationException("ההשכרה בוטלה");

        await _loanRepository.UpdateStatusAsync(id, LoanStatus.Returned, DateTime.UtcNow);

        // איחור בהחזרה: כל יום (או חלק מיום) מעבר למועד ההחזרה מחויב כיום השכרה נוסף
        var lateDays = loan.IsLocation ? 0 : IsraelTime.LateDays(loan.ExpectedReturnDate, IsraelTime.Now);

        // בהחזרה בשליח: אם השליח כבר אסף את הפריט מהשוכרת, איחור בהגעתו למשכירה אינו באשמת השוכרת ולא מחויב
        if (lateDays > 0 && await CourierHasItemAsync(loan))
            lateDays = 0;

        if (lateDays > 0)
        {
            await _loanRepository.UpdateLateDaysAsync(id, lateDays);
            loan.LateDays = lateDays;
        }

        if (!string.IsNullOrEmpty(loan.ItemCopyId))
        {
            var item = await _itemRepository.GetByIdAsync(loan.ItemCopyId);
            if (item is not null)
            {
                // הפריט חוזר להיות זמין (אלא אם השכרה אחרת שלו כבר התחילה)
                await _schedule.RefreshStatusAsync(item.Id!);

                // לעניין המיילים: החזרה בתוך תקופת החסד (90 דקות) נחשבת "בזמן" - השוכרת מקבלת מייל תודה, ולא מייל איחור
                if (lateDays > 0)
                {
                    LateCharge? lateCharge = null;
                    if (loan.PaymentMethod == PaymentMethod.CreditCard && loan.PaymentStatus == PaymentStatus.Paid)
                    {
                        var payer = !string.IsNullOrEmpty(loan.BorrowerId) ? await _userRepository.GetByIdAsync(loan.BorrowerId) : null;
                        var amount = lateDays * loan.RateOr(item.DailyPrice) * loan.Units;
                        var result = payer is null
                            ? new ExtraChargeResult { Error = "השוכרת לא נמצאה" }
                            : await _extraCharges.ChargeAsync(payer, amount, $"איחור בהחזרה - {item.Name}");
                        if (result.Success)
                        {
                            await _loanRepository.UpdateLateFeeChargedAsync(id, amount);
                            loan.LateFeeCharged = amount;
                        }
                        lateCharge = new LateCharge(result.Success);
                    }

                    await SendLateReturnEmailsAsync(loan, item, lateDays, lateCharge);
                }
                else
                    await SendReturnConfirmedEmailAsync(loan, item);
            }
        }
    }

    private readonly IMarkerRepository _markers;
    private readonly IItemScheduleService _schedule;

    // תחילת ההשכרה המבוקשת (תאריך + שעה) - לבדיקת חפיפה עם השכרות וימים חסומים
    // אקספרס מגיע תוך כ-3 שעות: מזמינים אותו 3.5 שעות לפני ההתחלה, ולא לפני 07:00
    private static DateTime ExpressDispatchTime(DateTime startDate, string? startTime)
    {
        var at = RequestedStart(startDate, startTime) - TimeSpan.FromHours(3.5);
        var earliest = startDate.Date.AddHours(7);
        return at < earliest ? earliest : at;
    }

    public async Task DispatchPendingExpressAsync()
    {
        var now = IsraelTime.Now;
        var due = (await _loanRepository.GetAllAsync())
            .Where(l => l.ExpressDispatchAtLocal is { } at && at <= now && string.IsNullOrEmpty(l.OutboundProviderId)
                        && l.Status == LoanStatus.Active && l.DeliveryMethod == DeliveryMethod.Delivery)
            .ToList();

        // משלוח אחד להזמנה מרובת פריטים: לפי שוכרת + מועד התחלה
        foreach (var group in due.GroupBy(l => (l.BorrowerId, l.StartDate)))
        {
            var first = group.First();
            var item = !string.IsNullOrEmpty(first.ItemCopyId) ? await _itemRepository.GetByIdAsync(first.ItemCopyId) : null;
            var owner = !string.IsNullOrEmpty(item?.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
            var borrower = !string.IsNullOrEmpty(first.BorrowerId) ? await _userRepository.GetByIdAsync(first.BorrowerId) : null;
            try
            {
                if (item is null || borrower is null || string.IsNullOrWhiteSpace(first.DeliveryAddress))
                    throw new InvalidOperationException("חסרים פרטי פריט, שוכרת או כתובת");
                var ownerAddress = OwnerShippingAddress($"{item.Address}, {item.City}".Trim(',', ' '), owner);
                var shipment = await _shippingService.RequestPickupAsync(ownerAddress.WithContact(owner?.Name, owner?.Phone),
                    ShippingAddressDto.FromFreeText(first.DeliveryAddress).WithContact(borrower.Name, borrower.Phone),
                    first.StartDate.Date, isExpress: true);
                foreach (var loan in group)
                    await _loanRepository.UpdateOutboundDispatchAsync(loan.Id!, shipment.TrackingRef, shipment.ProviderId, null);
            }
            catch (Exception ex)
            {
                foreach (var loan in group)
                    await _loanRepository.UpdateOutboundDispatchAsync(loan.Id!, null, null, null);
                try
                {
                    await _emailService.SendAsync(InboxEmail, "common", $"הזמנת שליח אקספרס נכשלה: {item?.Name}",
                        $"<div dir=\"rtl\"><p>השכרה של {System.Net.WebUtility.HtmlEncode(borrower?.Name)} ({System.Net.WebUtility.HtmlEncode(borrower?.Phone)}) - " +
                        $"{System.Net.WebUtility.HtmlEncode(item?.Name)}, מתחילה ב-{first.StartDate:dd/MM HH:mm}. הזמנת השליח נכשלה: {System.Net.WebUtility.HtmlEncode(ex.Message)}. צריך להזמין ידנית או לתאם עם השוכרת.</p></div>");
                }
                catch
                {
                    // לא קריטי
                }
            }
        }
    }

    private static DateTime RequestedStart(DateTime startDate, string? startTime) =>
        startDate.Date + (NormalizeTimeOfDay(startTime) is { } t ? TimeSpan.Parse(t, CultureInfo.InvariantCulture) : TimeSpan.Zero);

    // החזרה באותו יום (שעת ההחזרה אחרי שעת ההתחלה) מתומחרת כיום אחד; אחרת - תאריך ההתחלה + מספר הימים
    // מחיר הזמנת סטודיו/לוקיישן: שעות x מחיר לשעה, ולא יותר מהמחיר ליום שלם
    private static decimal LocationPrice(Item item, DateTime start, DateTime end)
    {
        var hours = (decimal)(end - start).TotalHours;
        if (end.Date != start.Date || hours <= 0)
            throw new ArgumentException("בסטודיו ולוקיישן ההזמנה היא ביום אחד - שעת הסיום צריכה להיות אחרי שעת ההתחלה");
        var minHours = item.MinRentalUnit == RentalUnit.Hours ? Math.Max(1, item.MinRentalAmount) : 1;
        if (hours < minHours)
            throw new ArgumentException($"מינימום הזמנה למקום הזה: {minHours} שעות");
        var hourly = item.HourlyPrice ?? item.DailyPrice;
        var price = Math.Round(hours * hourly, 2, MidpointRounding.AwayFromZero);
        return item.DailyPrice > 0 ? Math.Min(price, item.DailyPrice) : price;
    }

    private static DateTime SameDayReturnDate(bool sameDay, DateTime startDate, int rentalDays, string? startTime, TimeSpan returnTime)
    {
        if (sameDay && rentalDays == 1)
        {
            if (startDate.Date + returnTime <= RequestedStart(startDate, startTime))
                throw new ArgumentException("בהחזרה באותו יום - שעת ההחזרה צריכה להיות אחרי שעת ההתחלה");
            return startDate.Date + returnTime;
        }
        return startDate.AddDays(rentalDays).Date + returnTime;
    }

    // מייל לשוכרת כשההחזרה אושרה בזמן (עד חצי שעה אחרי מועד ההחזרה): תודה והזמנה להשכיר שוב. החזרה מאוחרת יותר מקבלת במקומו את מייל "הוחזר באיחור".
    private async Task SendReturnConfirmedEmailAsync(Loan loan, Item item)
    {
        if (!_emailService.IsEnabled || string.IsNullOrEmpty(loan.BorrowerId))
            return;

        try
        {
            var borrower = await _userRepository.GetByIdAsync(loan.BorrowerId!);
            if (borrower is null || string.IsNullOrWhiteSpace(borrower.Email))
                return;

            string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var baseUrl = _clientOptions.BaseUrl.TrimEnd('/');
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>שלום {H(NameText.FirstName(borrower.Name))},</p>" +
                       $"<p>קיבלנו אישור ש<strong>{H(LoanText.Name(item, loan))}</strong> הוחזר למשכירה בהצלחה, וההשכרה הסתיימה.</p>" +
                       "<p><strong>תודה שהשכרת דרך common!</strong></p>" +
                       $"<p>איך היה עם <strong>{H(item.Name)}</strong>? נשמח לדירוג קצר של הפריט.</p>" +
                       $"<p><a href=\"{baseUrl}/profile?review={Uri.EscapeDataString(loan.Id ?? string.Empty)}\" style=\"display:inline-block;background:#fff0cf;border:1px solid #000;border-radius:20px;padding:8px 18px;color:#000;text-decoration:none\"><strong>★ לדירוג ההשכרה</strong></a></p>" +
                       (await IsFirstReturnedAsBorrowerAsync(borrower.Id!) ? FirstRentalQuestionHtml(asOwner: false, baseUrl) : string.Empty) +
                       "<p>צריכה עוד ציוד? " +
                       $"<a href=\"{baseUrl}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:8px 18px;color:#000;text-decoration:none\"><strong>לקטלוג הציוד</strong></a></p>" +
                       "<p>common</p></div>";
            await _emailService.SendAsync(borrower.Email, borrower.Name, "ההחזרה אושרה - תודה! - common", html);
            if (!string.IsNullOrEmpty(loan.Id))
                await _markers.MarkAsync($"reviewreq_renter_{loan.Id}");
        }
        catch
        {
            // מייל שלא נשלח אינו סיבה לבטל החזרה שכבר נרשמה
        }
    }

    // מייל לשוכרת ולמשכירה כשהציוד הוחזר באיחור: כמה ימים וכמה לתשלום (תשלום ישירות למשכיר)
    private sealed record LateCharge(bool Charged);

    private async Task SendLateReturnEmailsAsync(Loan loan, Item item, int lateDays, LateCharge? lateCharge)
    {
        try
        {
            var h = (string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var fee = lateDays * loan.RateOr(item.DailyPrice) * loan.Units;
            var days = lateDays == 1 ? "יום אחד" : $"{lateDays} ימים";
            var borrowerPayText = lateCharge is null
                ? $"ולכן יש לשלם למשכירה תשלום נוסף של <strong>₪{fee:0.00}</strong>."
                : lateCharge.Charged
                    ? $"ולכן כרטיס האשראי שלך חויב ב-<strong>₪{fee:0.00}</strong>."
                    : $"ולכן יחויב כרטיס האשראי שלך ב-<strong>₪{fee:0.00}</strong>. לא הצלחנו לחייב אותו כרגע, ונחזור אלייך.";

            var borrower = !string.IsNullOrEmpty(loan.BorrowerId) ? await _userRepository.GetByIdAsync(loan.BorrowerId) : null;
            var owner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;

            if (borrower is not null && !string.IsNullOrWhiteSpace(borrower.Email))
            {
                var html = $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>שלום {h(NameText.FirstName(borrower.Name))},</p>" +
                           $"<p>הציוד <strong>{h(LoanText.Name(item, loan))}</strong> הוחזר באיחור של {days}. לפי תנאי האתר כל יום איחור מחויב כיום השכרה נוסף, {borrowerPayText}</p>" +
                           (await IsFirstReturnedAsBorrowerAsync(borrower.Id!) ? FirstRentalQuestionHtml(asOwner: false, _clientOptions.BaseUrl.TrimEnd('/')) : string.Empty) +
                           "<p>תודה שאתן חלק מקהילת common.</p></div>";
                await _emailService.SendAsync(borrower.Email, borrower.Name, "הציוד הוחזר באיחור - common", html);
            }

            if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
            {
                var html = $"<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\"><p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                           $"<p>הציוד <strong>{h(LoanText.Name(item, loan))}</strong> הוחזר אלייך באיחור של {days}. לפי תנאי האתר תקבלי תשלום נוסף של <strong>₪{fee:0.00}</strong> (יום השכרה נוסף על כל יום איחור).</p>" +
                           "<p>הציוד זמין שוב בקטלוג.</p>" +
                           "<p>תודה שאת חלק מקהילת common.</p></div>";
                await _emailService.SendAsync(owner.Email, owner.Name, "הציוד הוחזר באיחור - common", html);
            }
        }
        catch
        {
            // מייל שלא נשלח אינו סיבה להכשיל החזרה
        }
    }

    public async Task UpdateStatusAsync(string id, string status)
    {
        if (!Enum.TryParse<LoanStatus>(status, ignoreCase: true, out var parsedStatus))
            throw new ArgumentException($"סטטוס לא תקין: {status}");

        await _loanRepository.UpdateStatusAsync(id, parsedStatus);
    }

    public async Task ConfirmPaymentAsync(string id)
    {
        var loan = await _loanRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");

        if (loan.PaymentStatus == PaymentStatus.Paid)
            return;

        await _loanRepository.UpdatePaymentStatusAsync(id, PaymentStatus.Paid);

        if (!string.IsNullOrEmpty(loan.ItemCopyId))
        {
            loan.Item = await _itemRepository.GetByIdAsync(loan.ItemCopyId);
        }
        loan.PaymentStatus = PaymentStatus.Paid;

        var owner = !string.IsNullOrEmpty(loan.Item?.OwnerId)
            ? await _userRepository.GetByIdAsync(loan.Item.OwnerId)
            : null;

        // בהעברה בנקאית עם אסמכתא המייל "התקבל תשלום" כבר נשלח עם יצירת ההשכרה
        if (!(loan.PaymentMethod == PaymentMethod.BankTransfer && !string.IsNullOrEmpty(loan.PaymentProofId)))
            await SendPaymentReceivedEmailAsync(loan, owner);
    }

    // ===== מיילים לאחר אישור ההשכרה: אחד לשוכרת ואחד למשכירה =====
    // נשלחים תמיד. השוכרת מקבלת: פרטי כתובת האיסוף המדויקים (איסוף עצמי) או קישור מעקב והערכת הגעה
    // (משלוח), ובמזומן - הסכום לתשלום בצורה בולטת. המשכירה מקבלת: תזכורת לוודא את התשלום במזומן עם
    // קישור "חלון הסימון", ובהעברה בנקאית - האסמכתא מצורפת. שם השוכרת לא נחשף למשכירה, ופרטי הקשר של
    // המשכירה נחשפים לשוכרת רק לאחר שהתשלום הושלם. תקלה בשליחת מייל לעולם לא מכשילה השכרה שכבר נוצרה.
    private string ActionLink(Loan loan) =>
        $"{_clientOptions.BaseUrl.TrimEnd('/')}/loan-action/{loan.Id}?t={loan.OwnerActionToken}";

    private static string EstimatedArrivalText(Loan loan)
    {
        var date = loan.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return loan.ExpressDelivery
            ? "משלוח אקספרס - הערכה: הגעה תוך כ-3 שעות מרגע אישור ההשכרה"
            : string.IsNullOrEmpty(loan.StartTime)
                ? $"הערכה: הגעה ביום תחילת ההשכרה ({date})"
                : $"הגעה ביום תחילת ההשכרה ({date}) עד השעה {loan.StartTime}";
    }

    // קישור למסך "ההזמנה בוצעה": פרטי ההזמנה, מעקב משלוח וזמן הגעה (הלוך וחזור). דורש התחברות לחשבון
    private string OrderLink(Loan loan) => $"{_clientOptions.BaseUrl.TrimEnd('/')}/order/{loan.Id}";

    private static string OrderButtonHtml(string href) =>
        $"<p style=\"margin:14px 0\"><a href=\"{System.Net.WebUtility.HtmlEncode(href)}\" style=\"display:inline-block;background:#e89c9c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>לצפייה בהזמנה, בזמן ההגעה ובמעקב המשלוח</strong></a></p>";

    // קישור לשינוי אופן ההחזרה: "שינוי לאיסוף במשלוח" / "שינוי לאיסוף עצמי". דורש התחברות לחשבון.
    // נכלל במיילי התזכורת בלבד - לא במייל הראשוני של אישור ההשכרה
    public static string ReturnChangeLinkHtml(string baseUrl, Loan loan)
    {
        var href = System.Net.WebUtility.HtmlEncode($"{baseUrl.TrimEnd('/')}/order/{loan.Id}/return");
        var text = loan.ReturnMethod == ReturnMethod.CourierPickup
            ? "שינוי לאיסוף עצמי"
            : "שינוי לאיסוף במשלוח";
        return $"<p style=\"margin:8px 0\"><a href=\"{href}\">{text}</a></p>";
    }

    // תיאור אופן ההחזרה: איסוף עצמי מהשוכרת אל המשכירה או שליח. נכתב לפי הצד שקורא (שוכרת/משכירה)
    private static string ReturnSummaryHtml(Loan loan, bool forRenter)
    {
        var date = loan.ExpectedReturnDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        if (loan.ReturnMethod == ReturnMethod.CourierPickup)
        {
            return forRenter
                ? $"<p style=\"margin:6px 0 0\"><strong>החזרה:</strong> שליח יאסוף את הפריט {(string.IsNullOrWhiteSpace(loan.ReturnPickupAddress) ? "מכתובת המשלוח" : "מ" + System.Net.WebUtility.HtmlEncode(loan.ReturnPickupAddress))} ביום ההחזרה ({date}) ויחזיר אותו למשכירה עד השעה {loan.ExpectedReturnDate:HH:mm}. את חלון האיסוף ואת השליח אפשר לראות במסך ההזמנה.</p>"
                : $"<p style=\"margin:6px 0 0\"><strong>החזרה:</strong> שליח יאסוף את הפריט מהשוכרת ביום ההחזרה ({date}) ויחזיר אליך עד השעה {loan.ExpectedReturnDate:HH:mm}. את חלון ההגעה ואת השליח אפשר לראות במסך ההזמנה.</p>";
        }
        return forRenter
            ? "<p style=\"margin:6px 0 0\"><strong>החזרה:</strong> החזרה עצמית - את מחזירה את הפריט בעצמך למשכירה.</p>"
            : "<p style=\"margin:6px 0 0\"><strong>החזרה:</strong> החזרה עצמית - השוכרת תחזיר את הפריט אלייך.</p>";
    }

    // מה המשכירה צריכה לדעת על הוצאת הפריט: איסוף עצמי של השוכרת או שליח שיאסוף ממנה
    private static string OwnerShippingHtml(Loan loan)
    {
        if (loan.DeliveryMethod == DeliveryMethod.SelfPickup)
            return "<p style=\"margin:6px 0 0\"><strong>קבלה:</strong> איסוף עצמי - השוכרת תגיע לאסוף את הפריט מהכתובת שלך.</p>";

        var express = loan.ExpressDelivery
            ? "שליח אקספרס יגיע לאסוף את הפריט תוך כ-3 שעות מרגע אישור ההשכרה"
            : $"שליח יגיע לאסוף את הפריט ביום תחילת ההשכרה ({loan.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}){(string.IsNullOrEmpty(loan.StartTime) ? string.Empty : " עד השעה " + loan.StartTime)}";
        return $"<p style=\"margin:6px 0 0\"><strong>קבלה:</strong> משלוח - {express}. נא להכין את הפריט ארוז מראש.</p>";
    }

    // "ההשכרה הראשונה" - בלי מייל נוסף: חגיגה בתוך מייל אישור ההשכרה הרגיל
    private async Task<int> PriorRentalsAsBorrowerAsync(string borrowerId, IReadOnlyList<Loan> current)
    {
        var currentIds = current.Select(l => l.Id).ToHashSet();
        return (await _loanRepository.GetByBorrowerIdAsync(borrowerId))
            .Count(l => !currentIds.Contains(l.Id) && l.Status != LoanStatus.Cancelled);
    }

    private async Task<int> PriorRentalsAsOwnerAsync(string ownerId, IReadOnlyList<Loan> current)
    {
        var itemIds = (await _itemRepository.GetByOwnerIdAsync(ownerId)).Select(i => i.Id).ToHashSet();
        var currentIds = current.Select(l => l.Id).ToHashSet();
        return (await _loanRepository.GetAllAsync())
            .Count(l => itemIds.Contains(l.ItemCopyId) && !currentIds.Contains(l.Id) && l.Status != LoanStatus.Cancelled);
    }

    // אבני דרך: ההשכרה ה-10, 25, 50, 100 (גם כשהזמנה של כמה פריטים עוברת את המספר)
    private static readonly int[] Milestones = { 10, 25, 50, 100 };

    private static int? MilestoneReached(int prior, int added) =>
        Milestones.Where(m => prior < m && prior + added >= m).Cast<int?>().LastOrDefault();

    internal static string ShareLink(string baseUrl) =>
        "https://wa.me/?text=" + Uri.EscapeDataString($"גיליתי את קומון - קהילת צלמות שמשכירות ציוד צילום אחת לשנייה 📷 {baseUrl}");

    // שאלה אחרי ההשכרה הראשונה - בתוך מייל הסיכום הקיים. התשובה חוזרת למייל האתר (Reply-To)
    internal static string FirstRentalQuestionHtml(bool asOwner, string siteUrl) =>
        $"<div style=\"{CelebrationStyle}\">" +
        "<p style=\"margin:0\"><strong>ונשמח לשמוע ממך 💛</strong></p>" +
        (asOwner
            ? "<p style=\"margin:6px 0 0\">זו הייתה ההשכרה הראשונה של הציוד שלך בקומון. "
            : "<p style=\"margin:6px 0 0\">זו הייתה ההשכרה הראשונה שלך בקומון. ") +
        "מה הכי אהבת? ומה היה חסר לך? פשוט השיבי למייל הזה - אנחנו קוראות כל תשובה.</p>" +
        $"<p style=\"margin:8px 0 0\">ואם נהנית - <a href=\"{System.Net.WebUtility.HtmlEncode(ShareLink(siteUrl))}\">ספרי לחברות הצלמות שלך על קומון בוואטסאפ</a></p></div>";

    private async Task<bool> IsFirstReturnedAsBorrowerAsync(string borrowerId) =>
        (await _loanRepository.GetByBorrowerIdAsync(borrowerId)).Count(l => l.Status == LoanStatus.Returned) == 1;

    private const string CelebrationStyle = "background:#fff0cf;border:1px solid #000;border-radius:16px;padding:14px 16px;margin:12px 0";

    private async Task SendRentalConfirmationEmailsAsync(IReadOnlyList<Loan> loans, User? owner, User borrower, EmailAttachment? proof = null)
    {
        if (loans.Count == 0)
            return;

        try
        {
            var first = loans[0];
            var h = (string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var money = (decimal value) => "₪" + value.ToString("0.##", CultureInfo.InvariantCulture);

            var itemsHtml = string.Join("", loans.Select(l => $"<li>{h(LoanText.Name(l.Item, l))}</li>"));
            var itemsTotal = loans.Sum(l => l.RentalDays * l.RateOr(l.Item?.DailyPrice ?? 0) * l.Units);
            var total = itemsTotal + loans.Sum(l => l.DeliveryFee + l.ReturnFee);
            var startText = first.StartDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) +
                            (string.IsNullOrEmpty(first.StartTime) ? string.Empty : $" בשעה {first.StartTime}");
            var returnText = first.ExpectedReturnDate.ToString("dd/MM/yyyy 'בשעה' HH:mm", CultureInfo.InvariantCulture);
            var paid = first.PaymentStatus == PaymentStatus.Paid;
            var isPickup = first.DeliveryMethod == DeliveryMethod.SelfPickup;
            var isCash = first.PaymentMethod == PaymentMethod.Cash;
            var hasProof = first.PaymentMethod == PaymentMethod.BankTransfer && !string.IsNullOrEmpty(first.PaymentProofId);

            // השוכרת רואה "שולם" בהעברה בנקאית מיד - אישור המשכירה הוא סימון פנימי בלבד ואינו משנה לשוכרת.
            // המשכירה רואה שהתשלום ממתין לאישורה
            var paymentText = first.PaymentMethod == PaymentMethod.BankTransfer
                ? "העברה בנקאית (שולם)"
                : $"{PaymentMethodLabelHe(first.PaymentMethod)} ({(paid ? "שולם" : "ממתין לתשלום")})";
            var ownerPaymentText = hasProof && !paid
                ? "העברה בנקאית (הועלתה אסמכתא - ממתין לאישורך על קבלת התשלום)"
                : paymentText;

            // ---- אופן הקבלה ----
            string receivingHtml;
            if (isPickup)
            {
                var place = string.Join(", ", new[] { PickupAddressLine(first.Item?.Address, owner), first.Item?.City }.Where(x => !string.IsNullOrWhiteSpace(x)));
                receivingHtml =
                    "<div style=\"border:2px solid #000;border-radius:12px;padding:12px;margin:12px 0\">" +
                    (first.IsLocation ? "<p style=\"margin:0 0 4px\"><strong>כתובת המקום:</strong></p>" : "<p style=\"margin:0 0 4px\"><strong>איסוף עצמי - כתובת האיסוף:</strong></p>") +
                    $"<p style=\"margin:0;font-size:18px\"><strong>{h(place)}</strong></p>" +
                    (string.IsNullOrWhiteSpace(owner?.AddressNotes)
                        ? string.Empty
                        : $"<p style=\"margin:6px 0 0\">הערות על הכתובת: <strong>{h(owner!.AddressNotes!)}</strong></p>") +
                    $"<p style=\"margin:6px 0 0\">{(first.IsLocation ? "מועד הצילום" : "מועד האיסוף")}: <strong>{h(startText)}</strong></p></div>";
            }
            else
            {
                var tracking = first.OutboundTrackingId;
                var trackingHtml = string.IsNullOrWhiteSpace(tracking)
                    ? "<p style=\"margin:6px 0 0\">קישור מעקב ישלח אליך כשהמשלוח ייצא לדרך.</p>"
                    : tracking.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? $"<p style=\"margin:6px 0 0\"><a href=\"{h(tracking)}\">למעקב אחרי המשלוח אצל חברת השילוח - לחצי כאן</a></p>"
                        : $"<p style=\"margin:6px 0 0\">מספר מעקב: <strong>{h(tracking)}</strong></p>";

                receivingHtml =
                    "<div style=\"border:2px solid #000;border-radius:12px;padding:12px;margin:12px 0\">" +
                    "<p style=\"margin:0 0 4px\"><strong>משלוח לכתובת:</strong></p>" +
                    $"<p style=\"margin:0;font-size:18px\"><strong>{h(first.DeliveryAddress)}</strong></p>" +
                    $"<p style=\"margin:6px 0 0\">{h(EstimatedArrivalText(first))}</p>" +
                    trackingHtml + "</div>";
            }

            string Table(string extraRows, string payText) =>
                "<table style=\"border-collapse:collapse\" cellpadding=\"6\">" +
                $"<tr><td><strong>תחילת ההשכרה</strong></td><td>{h(startText)}</td></tr>" +
                (first.IsLocation
                    ? $"<tr><td><strong>סיום</strong></td><td>{h(returnText)}</td></tr>"
                    : $"<tr><td><strong>החזרה</strong></td><td>{h(returnText)} ({first.RentalDays} ימים)</td></tr>") +
                $"<tr><td><strong>תשלום</strong></td><td>{h(payText)}</td></tr>" +
                $"<tr><td><strong>סה\"כ</strong></td><td>{money(total)}</td></tr>" +
                extraRows + "</table>";

            // ---- לשוכרת ----
            if (!string.IsNullOrWhiteSpace(borrower.Email))
            {
                var cashBox = isCash
                    ? "<div style=\"background:#ffe2e2;border:3px solid #c00;border-radius:12px;padding:14px;margin:12px 0;text-align:center\">" +
                      $"<p style=\"margin:0;font-size:22px;color:#c00\"><strong>יש לשלם במזומן: {money(itemsTotal)}</strong></p>" +
                      "<p style=\"margin:6px 0 0\">התשלום במזומן למשכירה בעת קבלת הפריט (אפשרי באיסוף עצמי בלבד).</p></div>"
                    : string.Empty;

                var renterPrior = string.IsNullOrEmpty(borrower.Id) ? -1 : await PriorRentalsAsBorrowerAsync(borrower.Id, loans);
                var renterFirst = renterPrior == 0;
                var renterMilestone = renterPrior > 0 ? MilestoneReached(renterPrior, loans.Count) : null;
                var renterCelebration = renterFirst
                    ? $"<div style=\"{CelebrationStyle}\"><p style=\"margin:0;font-size:18px\"><strong>🎉 ההשכרה הראשונה שלך בקומון!</strong></p>" +
                      "<p style=\"margin:6px 0 0\">שמחות שהצטרפת לקהילה שלנו. מאחלות לך צילומים מוצלחים ומלאי השראה!</p></div>"
                    : renterMilestone is int rm
                        ? $"<div style=\"{CelebrationStyle}\"><p style=\"margin:0;font-size:18px\"><strong>זו ההשכרה ה-{rm} שלך בקומון!</strong></p>" +
                          "<p style=\"margin:6px 0 0\">תודה שאת חלק מהקהילה ובוחרת לשכור מצלמות אחרות.</p></div>"
                        : string.Empty;
                var renterHtml =
                    "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                    $"<p>שלום {h(NameText.FirstName(borrower.Name))},</p>" +
                    renterCelebration +
                    "<p>ההשכרה אושרה! הפריטים שלך:</p>" +
                    $"<ul>{itemsHtml}</ul>" +
                    receivingHtml + ReturnSummaryHtml(first, forRenter: true) + cashBox + Table(string.Empty, paymentText) +
                    OrderButtonHtml(OrderLink(first)) +
                    $"<p>אפשר לעקוב אחרי ההשכרה בכל עת {ProfileLink} באתר common.</p></div>";
                await _emailService.SendAsync(borrower.Email, borrower.Name,
                    renterFirst ? "🎉 ההשכרה הראשונה שלך אושרה! - common" : "ההשכרה אושרה - common", renterHtml);
            }

            // ---- למשכירה ----
            if (owner is not null && !string.IsNullOrWhiteSpace(owner.Email))
            {
                var links = string.Join("", loans.Select(l =>
                    $"<p style=\"margin:6px 0\"><a href=\"{h(ActionLink(l))}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:8px 16px;color:#000;text-decoration:none\"><strong>סימון שהפריט נאסף ממך - {h(l.Item?.Name ?? "פריט")}</strong></a></p>"));

                var ownerBox = isCash
                    ? "<div style=\"background:#ffe2e2;border:3px solid #c00;border-radius:12px;padding:14px;margin:12px 0;text-align:center\">" +
                      $"<p style=\"margin:0;font-size:20px;color:#c00\"><strong>בעת איסוף הפריט ודאי שהתקבל תשלום במזומן: {money(itemsTotal)}</strong></p>" +
                      "<p style=\"margin:6px 0 0\">לסימון שהתשלום התקבל - לחצי על הקישור למטה ובחלון שייפתח סמני \"התשלום התקבל\" (וגם \"הפריט נאסף\" כשהוא נאסף).</p></div>"
                    : hasProof
                        ? "<p><strong>התשלום בוצע בהעברה בנקאית.</strong> מצורפת האסמכתא שהשוכרת העלתה, והיא זמינה גם באזור האישי.</p>"
                        : string.Empty;

                var ownerPrior = string.IsNullOrEmpty(owner.Id) ? -1 : await PriorRentalsAsOwnerAsync(owner.Id, loans);
                var ownerFirst = ownerPrior == 0;
                var ownerMilestone = ownerPrior > 0 ? MilestoneReached(ownerPrior, loans.Count) : null;
                var siteUrl = _clientOptions.BaseUrl.TrimEnd('/');
                var ownerCelebration = ownerFirst
                    ? $"<div style=\"{CelebrationStyle}\"><p style=\"margin:0;font-size:18px\"><strong>🎉 ההשכרה הראשונה של הציוד שלך!</strong></p>" +
                      "<p style=\"margin:6px 0 0\">מזל טוב! הציוד שלך מתחיל להרוויח בשבילך ולעזור לצלמת נוספת בקהילה.</p>" +
                      $"<p style=\"margin:8px 0 0\">מכירה עוד צלמות שישמחו לקבל תמורה לציוד ששוכב בארון? <a href=\"{h(ShareLink(siteUrl))}\">ספרי להן על קומון</a></p></div>"
                    : ownerMilestone is int om
                        ? $"<div style=\"{CelebrationStyle}\"><p style=\"margin:0;font-size:18px\"><strong>הציוד שלך הושכר בפעם ה-{om}!</strong></p>" +
                          "<p style=\"margin:6px 0 0\">תודה שאת משתפת את הציוד שלך ועוזרת לצלמות אחרות בקהילה.</p></div>"
                        : string.Empty;
                var ownerHtml =
                    "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                    $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                    ownerCelebration +
                    "<p>נפתחה השכרה חדשה על הציוד שלך:</p>" +
                    $"<ul>{itemsHtml}</ul>" +
                    Table(string.Empty, ownerPaymentText) + OwnerShippingHtml(first) + ReturnSummaryHtml(first, forRenter: false) + ownerBox +
                    OrderButtonHtml(OrderLink(first)) +
                    "<p style=\"margin:12px 0 4px\"><strong>כשהפריט נאסף ממך</strong> (על ידי השוכרת או השליח) - לחצי על הכפתור הירוק למטה, ובחלון שייפתח סמני \"הפריט נאסף\". בהשכרה במזומן סמני שם גם \"התשלום התקבל\".</p>" +
                    links +
                    $"<p>את הפרטים המלאים אפשר לראות {ProfileLink} באתר common.</p></div>";
                await _emailService.SendAsync(
                    owner.Email, owner.Name, ownerFirst ? "🎉 ההשכרה הראשונה של הציוד שלך! - common" : "השכרה חדשה על הציוד שלך - common", ownerHtml,
                    proof is null ? null : new[] { proof });

                if (hasProof && !paid)
                {
                    var confirmLinks = string.Join("", loans.Select(l =>
                        $"<p style=\"margin:6px 0\"><a href=\"{h(ActionLink(l))}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:8px 16px;color:#000;text-decoration:none\"><strong>אישור אסמכתא - {h(l.Item?.Name ?? "פריט")}</strong></a></p>"));
                    var paymentHtml =
                        "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                        $"<p>שלום {h(NameText.FirstName(owner.Name))},</p>" +
                        "<p>התקבל תשלום בהעברה בנקאית עבור:</p>" +
                        $"<ul>{itemsHtml}</ul>" +
                        $"<p>סכום: <strong>{money(itemsTotal)}</strong></p>" +
                        "<p>מצורפת האסמכתא שהשוכרת העלתה. אחרי שבדקת שההעברה נכנסה לחשבונך - לחצי על הכפתור לאישור.</p>" +
                        confirmLinks + "</div>";
                    await _emailService.SendAsync(
                        owner.Email, owner.Name, "התקבל תשלום עבור השכרה - common", paymentHtml,
                        proof is null ? null : new[] { proof });
                }
            }
        }
        catch
        {
            // מייל שלא נשלח אינו סיבה לבטל השכרה שכבר נוצרה
        }
    }

    private async Task SendPaymentReceivedEmailAsync(Loan loan, User? owner)
    {
        if (owner is null || string.IsNullOrWhiteSpace(owner.Email))
            return;

        var total = loan.RentalDays * loan.RateOr(loan.Item?.DailyPrice ?? 0) * loan.Units + loan.DeliveryFee + loan.ReturnFee;
        var html = $"""
            <p>שלום {System.Net.WebUtility.HtmlEncode(NameText.FirstName(owner.Name))},</p>
            <p>התקבל תשלום עבור השכרת <strong>{System.Net.WebUtility.HtmlEncode(LoanText.Name(loan.Item, loan, "הפריט שלך"))}</strong>{(loan.PaymentMethod == PaymentMethod.Cash ? ", בהתאם לסימונך" : string.Empty)}.</p>
            <p>סכום: <strong>₪{total:0.00}</strong> (אמצעי תשלום: {PaymentMethodLabelHe(loan.PaymentMethod)})</p>
            <p>ניתן לצפות בפרטים המלאים {ProfileLink} באתר common.</p>
            """;

        await _emailService.SendAsync(owner.Email, owner.Name, "התקבל תשלום עבור השכרה - common", html);
    }

    // גרסה מרוכזת של הודעת "התקבל תשלום" עבור הזמנה של כמה פריטים בבת אחת (ראו CreateBatchAsync) -
    // מייל אחד המפרט את כל הפריטים והסכום הכולל, במקום מייל נפרד לכל פריט
    private async Task SendOrderPaymentReceivedEmailAsync(IReadOnlyList<Loan> loans, User? owner)
    {
        if (owner is null || string.IsNullOrWhiteSpace(owner.Email) || loans.Count == 0)
            return;

        var itemsListHtml = string.Join(
            "",
            loans.Select(loan => $"<li>{System.Net.WebUtility.HtmlEncode(LoanText.Name(loan.Item, loan))}</li>"));

        var total = loans.Sum(loan => loan.RentalDays * loan.RateOr(loan.Item?.DailyPrice ?? 0) * loan.Units + loan.DeliveryFee + loan.ReturnFee);
        var paymentMethod = loans[0].PaymentMethod;

        var html = $"""
            <p>שלום {System.Net.WebUtility.HtmlEncode(NameText.FirstName(owner.Name))},</p>
            <p>התקבל תשלום עבור הזמנה הכוללת {loans.Count} פריטים:</p>
            <ul>{itemsListHtml}</ul>
            <p>סכום כולל: <strong>₪{total:0.00}</strong> (אמצעי תשלום: {PaymentMethodLabelHe(paymentMethod)})</p>
            <p>ניתן לצפות בפרטים המלאים {ProfileLink} באתר common.</p>
            """;

        await _emailService.SendAsync(owner.Email, owner.Name, "התקבל תשלום עבור הזמנה - common", html);
    }

    private static string PaymentMethodLabelHe(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "מזומן",
        PaymentMethod.BankTransfer => "העברה בנקאית",
        PaymentMethod.CreditCard => "כרטיס אשראי",
        _ => method.ToString()
    };

    private static DeliveryMethod ParseDeliveryMethod(string? value) =>
        Enum.TryParse<DeliveryMethod>(value, ignoreCase: true, out var parsed) ? parsed : DeliveryMethod.SelfPickup;

    private static ReturnMethod ParseReturnMethod(string? value) =>
        Enum.TryParse<ReturnMethod>(value, ignoreCase: true, out var parsed) ? parsed : ReturnMethod.SelfDropoff;

    private static PaymentMethod ParsePaymentMethod(string? value) =>
        Enum.TryParse<PaymentMethod>(value, ignoreCase: true, out var parsed) ? parsed : PaymentMethod.Cash;

    // שעה תקינה בפורמט HH:mm (אחרת null) - לשעת תחילת ההשכרה, שהיא אופציונלית
    // השעה שעד אליה הפריט חייב להגיע לשוכרת (שעת ההתחלה שביקשה) - null אם לא נבחרה שעה
    private async Task<ShippingChargeOutcome> ChargeShippingAsync(
        User payer, PaymentMethod orderPayment, decimal fee, string? paymentId, bool coveredByOrderCard, params string?[] bookedProviderIds)
    {
        if (fee <= 0)
            return new ShippingChargeOutcome("None", null);
        if (coveredByOrderCard && orderPayment == PaymentMethod.CreditCard)
            return new ShippingChargeOutcome("PaidWithOrder", null);

        try
        {
            return await _shippingPayments.ChargeAsync(payer, fee, paymentId);
        }
        catch
        {
            foreach (var providerId in bookedProviderIds.Where(p => !string.IsNullOrEmpty(p)))
            {
                try { await _shippingService.CancelAsync(providerId!); } catch { }
            }
            throw;
        }
    }

    private static void RequireOrderPrerequisites(bool termsAccepted, bool phoneConfirmed, User borrower)
    {
        if (!termsAccepted)
            throw new ArgumentException("יש לאשר את תנאי השימוש לפני אישור ההזמנה");
        if (new string((borrower.Phone ?? string.Empty).Where(char.IsDigit).ToArray()).Length < 9)
            throw new ArgumentException("יש להוסיף מספר טלפון תקין בחשבון לפני הזמנה");
        if (!phoneConfirmed)
            throw new ArgumentException("יש לאשר שמספר הטלפון שלך נכון ומתפקד");
    }

    private static TimeSpan? DeliveryDeadline(string? startTime)
    {
        var normalized = NormalizeTimeOfDay(startTime);
        return normalized is null ? null : TimeSpan.Parse(normalized, CultureInfo.InvariantCulture);
    }

    // היום עצמו: שעת ההתחלה לא יכולה להיות בעבר (סבלנות של 10 דקות לזמן מילוי הטופס)
    private static void EnsureStartNotPast(DateTime startDate, string? startTime)
    {
        var now = IsraelTime.Now;
        if (startDate.Date != now.Date)
            return;

        var normalized = NormalizeTimeOfDay(startTime);
        if (normalized is not null && TimeSpan.Parse(normalized, CultureInfo.InvariantCulture) < now.TimeOfDay - TimeSpan.FromMinutes(10))
            throw new ArgumentException("שעת ההתחלה שבחרת כבר עברה - נא לבחור שעה עתידית");
    }

    // כתובת איסוף ההחזרה: כתובת אחרת שנבחרה (רק כשהקבלה במשלוח והחזרה בשליח), אחרת כתובת המשלוח
    private static (ShippingAddressDto? Address, string? Text) ResolveReturnPickup(
        DeliveryMethod deliveryMethod, ReturnMethod returnMethod, DeliveryAddressDto? other, ShippingAddressDto? deliveryAddress)
    {
        if (returnMethod != ReturnMethod.CourierPickup || deliveryMethod != DeliveryMethod.Delivery || other is null)
            return (deliveryAddress, null);

        if (!IsCourierAddressComplete(other))
            throw new ArgumentException("יש להזין כתובת מלאה לאיסוף ההחזרה: עיר, רחוב, מספר בית, קומה ומספר דירה או שם משפחה");

        return (ShippingAddressDto.FromDetails(other), FormatDeliveryAddress(other));
    }

    private static string? NormalizeTimeOfDay(string? time) =>
        !string.IsNullOrWhiteSpace(time) &&
        TimeSpan.TryParseExact(time, "hh\\:mm", CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString("hh\\:mm", CultureInfo.InvariantCulture)
            : null;

    private static TimeSpan ParseReturnTime(string? returnTime)
    {
        if (!string.IsNullOrWhiteSpace(returnTime) &&
            TimeSpan.TryParseExact(returnTime, "hh\\:mm", CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return new TimeSpan(12, 0, 0);
    }

    private async Task<LoanResponseDto> MapToResponseAsync(Loan loan)
    {
        // שים לב: פרטי השוכר (Borrower) אינם נטענים כאן בכוונה - שם השוכר לעולם אינו נחשף למשכיר
        // ואינו מופיע ב-LoanResponseDto בשום מקרה.
        var item = !string.IsNullOrEmpty(loan.ItemCopyId)
            ? await _itemRepository.GetByIdAsync(loan.ItemCopyId)
            : null;

        loan.Item = item;

        return await MapToResponseWithOwnerAsync(loan);
    }

    // משלים את טעינת בעל הפריט - רק כאשר יש בפועל צורך להציג משהו משלו ללקוח: פרטי חשבון
    // (העברה בנקאית) או פרטי קשר לתיאום איסוף (איסוף עצמי ששולם בפועל) - ובונה את ה-DTO הסופי
    private async Task<LoanResponseDto> MapToResponseWithOwnerAsync(Loan loan)
    {
        User? owner = loan.Item?.Owner;

        var needsOwner =
            loan.PaymentMethod == PaymentMethod.BankTransfer ||
            loan.DeliveryMethod == DeliveryMethod.SelfPickup;

        if (owner is null && needsOwner && !string.IsNullOrEmpty(loan.Item?.OwnerId))
        {
            owner = await _userRepository.GetByIdAsync(loan.Item.OwnerId);
        }

        return MapToResponse(loan, owner);
    }

    private static LoanResponseDto MapToResponse(Loan loan, User? owner)
    {
        // שם וטלפון המשכירה לעולם אינם נחשפים לשוכרת (ולהפך) - כל התיאום עובר דרך האתר, כדי למנוע קשר מחוץ לו
        var isBankTransfer = loan.PaymentMethod == PaymentMethod.BankTransfer;

        return new LoanResponseDto
        {
            Id = loan.Id ?? string.Empty,
            ItemId = loan.ItemCopyId ?? string.Empty,
            ItemName = loan.Item is null ? string.Empty : LoanText.Name(loan.Item, loan),
            // שם השוכר לעולם אינו נכלל כאן - המשכיר אינו יכול לראות את שם השוכר בשום מקרה
            BorrowerId = loan.BorrowerId ?? string.Empty,
            LoanDate = loan.LoanDate,
            StartDate = loan.StartDate,
            RentalDays = loan.RentalDays,
            StartTime = loan.StartTime,
            // שעון קיר מקומי של ישראל (ללא Z) - כדי שהדפדפן יציג את השעה כפי שנקבעה ולא יזיז אותה
            ExpectedReturnDate = DateTime.SpecifyKind(loan.ExpectedReturnDate, DateTimeKind.Unspecified),
            LateDays = loan.LateDays,
            HasPaymentProof = !string.IsNullOrEmpty(loan.PaymentProofId),
            PickedUpAt = loan.PickedUpAt,
            LateFee = loan.LateDays * loan.RateOr(loan.Item?.DailyPrice ?? 0) * loan.Units,
            ActualReturnDate = loan.ActualReturnDate,
            Status = loan.Status.ToString(),
            DeliveryMethod = loan.DeliveryMethod.ToString(),
            ExpressDelivery = loan.ExpressDelivery,
            ItemCity = loan.Item?.City,
            // הכתובת המדויקת נחשפת רק כאשר אופן הקבלה הוא איסוף עצמי
            ItemAddress = loan.DeliveryMethod == DeliveryMethod.SelfPickup
                ? PickupAddressLine(loan.Item?.Address, owner)
                : null,
            ItemAddressNotes = loan.DeliveryMethod == DeliveryMethod.SelfPickup ? owner?.AddressNotes : null,
            PaymentMethod = loan.PaymentMethod.ToString(),
            PaymentStatus = loan.PaymentStatus.ToString(),
            PaymentTransactionId = loan.PaymentTransactionId,
            OwnerPayoutAccountHolderName = isBankTransfer ? owner?.PayoutAccountHolderName : null,
            OwnerPayoutBankName = isBankTransfer ? owner?.PayoutBankName : null,
            OwnerPayoutBranchNumber = isBankTransfer ? owner?.PayoutBranchNumber : null,
            OwnerPayoutAccountNumber = isBankTransfer ? owner?.PayoutAccountNumber : null,
            DeliveryFee = loan.DeliveryFee,
            ReturnFee = loan.ReturnFee,
            RentalPrice = loan.RentalDays * loan.RateOr(loan.Item?.DailyPrice ?? 0) * loan.Units,
            IsLocation = loan.IsLocation,
            Quantity = loan.Units,
            CancelDeadline = CancelDeadlineOf(loan),
            ReturnMethod = loan.ReturnMethod.ToString(),
            OutboundTrackingId = loan.OutboundTrackingId,
            ReturnTrackingId = loan.ReturnTrackingId,
            OutboundTrackable = !string.IsNullOrEmpty(loan.OutboundProviderId),
            ReturnTrackable = !string.IsNullOrEmpty(loan.ReturnProviderId),
            OrderGroupId = loan.OrderGroupId
        };
    }
}
