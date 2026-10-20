namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;

// מימוש שכבת הגישה לנתונים עבור עסקאות השכרה, מול Firestore REST API
public class LoanRepository : ILoanRepository
{
    private const string CollectionName = "Loans";
    private readonly FirebaseDbContext _firebase;

    public LoanRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<IReadOnlyList<Loan>> GetAllAsync()
    {
        var documents = await _firebase.ListDocumentsAsync(CollectionName);
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Loan>> GetByStatusAsync(LoanStatus status)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(Loan.Status), FirestoreJson.StringField(status.ToString())) });

        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Loan>> GetByExtensionStatusAsync(string extensionStatus)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(Loan.ExtensionStatus), FirestoreJson.StringField(extensionStatus)) });

        return documents.Select(ToEntity).ToList();
    }

    public async Task<Loan?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        return document is null ? null : ToEntity(document);
    }

    public async Task<IReadOnlyList<Loan>> GetActiveLoansForItemAsync(string itemId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[]
            {
                (nameof(Loan.ItemCopyId), FirestoreJson.StringField(itemId)),
                (nameof(Loan.Status), FirestoreJson.StringField(LoanStatus.Active.ToString()))
            });

        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Loan>> GetByBorrowerIdAsync(string borrowerId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(Loan.BorrowerId), FirestoreJson.StringField(borrowerId)) });

        return documents.Select(ToEntity).ToList();
    }

    public async Task<Loan> CreateAsync(Loan loan)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(loan));
        loan.Id = FirestoreJson.ExtractDocumentId(created);
        return loan;
    }

    // יוצר את ההשכרות ומעלה את גרסת הפריטים באותה פעולה אטומית, בתנאי שאף פריט לא השתנה מאז שנקרא.
    // אם מישהי אחרת השכירה אחד הפריטים בינתיים - נזרקת ConcurrencyConflictException ושום דבר לא נשמר
    public async Task<Loan> CreateWithItemCheckAsync(Loan loan, Item item, CancellationToken ct = default) =>
        (await CreateWithItemCheckAsync(new[] { (loan, item) }, ct))[0];

    public async Task<IReadOnlyList<Loan>> CreateWithItemCheckAsync(IReadOnlyList<(Loan Loan, Item Item)> bookings, CancellationToken ct = default)
    {
        var writes = new List<JsonObject>();
        var ids = new List<string>();

        foreach (var (loan, item) in bookings)
        {
            if (string.IsNullOrEmpty(item.Id) || string.IsNullOrEmpty(item.ConcurrencyToken))
                throw new InvalidOperationException("הפריט נטען בלי concurrency token");

            var loanId = FirebaseDbContext.NewDocumentId();
            ids.Add(loanId);
            writes.Add(_firebase.CreateWrite(CollectionName, loanId, ToFields(loan)));
            writes.Add(_firebase.GuardedPatchWrite("Items", item.Id,
                new JsonObject { [nameof(Item.Version)] = FirestoreJson.IntegerField(item.Version + 1) },
                item.ConcurrencyToken));
        }

        await _firebase.CommitAsync(writes, bookings[0].Item.Id, ct);

        for (var i = 0; i < bookings.Count; i++)
        {
            bookings[i].Loan.Id = ids[i];
            bookings[i].Item.Version++;
        }

        return bookings.Select(b => b.Loan).ToList();
    }

    public async Task UpdateStatusAsync(string id, LoanStatus status, DateTime? actualReturnDate = null)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.Status)] = FirestoreJson.StringField(status.ToString())
        };

        var fieldPaths = new List<string> { nameof(Loan.Status) };

        if (actualReturnDate.HasValue)
        {
            fields[nameof(Loan.ActualReturnDate)] = FirestoreJson.TimestampField(actualReturnDate.Value);
            fieldPaths.Add(nameof(Loan.ActualReturnDate));
        }

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, fieldPaths);
    }

    public async Task UpdatePaymentStatusAsync(string id, PaymentStatus status)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.PaymentStatus)] = FirestoreJson.StringField(status.ToString())
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.PaymentStatus) });
    }

    public async Task UpdateReturnReminderSentAsync(string id, DateTime sentAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.ReturnReminderSentAt)] = FirestoreJson.TimestampField(sentAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.ReturnReminderSentAt) });
    }

    public async Task UpdateLateDaysAsync(string id, int lateDays)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.LateDays)] = FirestoreJson.DoubleField(lateDays)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.LateDays) });
    }

    public Task DeleteAsync(string id) => _firebase.DeleteDocumentAsync(CollectionName, id);

    public async Task UpdatePickedUpAsync(string id, DateTime pickedUpAtUtc, string? by = null)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.PickedUpAt)] = FirestoreJson.TimestampField(pickedUpAtUtc),
            [nameof(Loan.PickedUpBy)] = FirestoreJson.StringOrNullField(by)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.PickedUpAt), nameof(Loan.PickedUpBy) });
    }

    public async Task UpdateReturnPromptSentAsync(string id, DateTime sentAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.ReturnPromptSentAt)] = FirestoreJson.TimestampField(sentAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.ReturnPromptSentAt) });
    }

    public async Task UpdateNotReturnedReportedAsync(string id, DateTime reportedAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.NotReturnedReportedAt)] = FirestoreJson.TimestampField(reportedAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.NotReturnedReportedAt) });
    }

    public async Task UpdateExtensionAsync(string id, string? status, int days, DateTime? requestedAtUtc, int attempts)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.ExtensionStatus)] = FirestoreJson.StringOrNullField(status),
            [nameof(Loan.ExtensionDays)] = FirestoreJson.DoubleField(days),
            [nameof(Loan.ExtensionRequestedAt)] = FirestoreJson.TimestampOrNullField(requestedAtUtc),
            [nameof(Loan.ExtensionAttempts)] = FirestoreJson.DoubleField(attempts)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields,
            new[] { nameof(Loan.ExtensionStatus), nameof(Loan.ExtensionDays), nameof(Loan.ExtensionRequestedAt), nameof(Loan.ExtensionAttempts) });
    }

    public async Task UpdateCancellationAsync(string id, DateTime cancelledAtUtc, string cancelledBy)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.CancelledAtUtc)] = FirestoreJson.TimestampField(cancelledAtUtc),
            [nameof(Loan.CancelledBy)] = FirestoreJson.StringField(cancelledBy)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields,
            new[] { nameof(Loan.CancelledAtUtc), nameof(Loan.CancelledBy) });
    }

    public async Task UpdateOwnerPayoutAsync(string id, DateTime paidAtUtc, decimal amount, string? proofId = null)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.OwnerPayoutPaidAtUtc)] = FirestoreJson.TimestampField(paidAtUtc),
            [nameof(Loan.OwnerPayoutAmount)] = FirestoreJson.DoubleField((double)amount),
            [nameof(Loan.OwnerPayoutProofId)] = FirestoreJson.StringOrNullField(proofId)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields,
            new[] { nameof(Loan.OwnerPayoutPaidAtUtc), nameof(Loan.OwnerPayoutAmount), nameof(Loan.OwnerPayoutProofId) });
    }

    public async Task UpdateShippingChargeAsync(string id, string? status, string? paymentId, decimal amount)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.ShippingChargeStatus)] = FirestoreJson.StringOrNullField(status),
            [nameof(Loan.ShippingPaymentId)] = FirestoreJson.StringOrNullField(paymentId),
            [nameof(Loan.ShippingChargedAmount)] = FirestoreJson.DoubleField((double)amount)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields,
            new[] { nameof(Loan.ShippingChargeStatus), nameof(Loan.ShippingPaymentId), nameof(Loan.ShippingChargedAmount) });
    }

    public async Task UpdateReturnPlanAsync(string id, ReturnMethod method, string? trackingId, string? providerId, string? pickupAddress, decimal returnFee)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.ReturnMethod)] = FirestoreJson.StringField(method.ToString()),
            [nameof(Loan.ReturnTrackingId)] = FirestoreJson.StringOrNullField(trackingId),
            [nameof(Loan.ReturnProviderId)] = FirestoreJson.StringOrNullField(providerId),
            [nameof(Loan.ReturnPickupAddress)] = FirestoreJson.StringOrNullField(pickupAddress),
            [nameof(Loan.ReturnFee)] = FirestoreJson.DoubleField((double)returnFee)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields,
            new[] { nameof(Loan.ReturnMethod), nameof(Loan.ReturnTrackingId), nameof(Loan.ReturnProviderId), nameof(Loan.ReturnPickupAddress), nameof(Loan.ReturnFee) });
    }

    public async Task UpdateLateFeeChargedAsync(string id, decimal amount)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.LateFeeCharged)] = FirestoreJson.DoubleField((double)amount)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.LateFeeCharged) });
    }

    public async Task ApplyExtensionAsync(string id, DateTime newExpectedReturnDate, int newRentalDays, decimal extensionChargedTotal)
    {
        var fields = new JsonObject
        {
            // מועדי השכרה נשמרים כשעון קיר ישראלי "כמו שהוא" (מסומן UTC, בלי המרה)
            [nameof(Loan.ExpectedReturnDate)] = FirestoreJson.TimestampField(DateTime.SpecifyKind(newExpectedReturnDate, DateTimeKind.Utc)),
            [nameof(Loan.RentalDays)] = FirestoreJson.DoubleField(newRentalDays),
            [nameof(Loan.ReturnReminderSentAt)] = FirestoreJson.NullField(),
            [nameof(Loan.ReturnPromptSentAt)] = FirestoreJson.NullField(),
            [nameof(Loan.ExtensionChargedTotal)] = FirestoreJson.DoubleField((double)extensionChargedTotal),
            [nameof(Loan.ExtensionStatus)] = FirestoreJson.StringField("Approved")
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields,
            new[] { nameof(Loan.ExpectedReturnDate), nameof(Loan.RentalDays), nameof(Loan.ReturnReminderSentAt), nameof(Loan.ReturnPromptSentAt), nameof(Loan.ExtensionChargedTotal), nameof(Loan.ExtensionStatus) });
    }

    public async Task UpdateOutboundDispatchAsync(string id, string? trackingId, string? providerId, DateTime? expressDispatchAtLocal)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.OutboundTrackingId)] = FirestoreJson.StringOrNullField(trackingId),
            [nameof(Loan.OutboundProviderId)] = FirestoreJson.StringOrNullField(providerId),
            [nameof(Loan.ExpressDispatchAtLocal)] = FirestoreJson.TimestampOrNullField(expressDispatchAtLocal is null ? null : DateTime.SpecifyKind(expressDispatchAtLocal.Value, DateTimeKind.Utc))
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields,
            new[] { nameof(Loan.OutboundTrackingId), nameof(Loan.OutboundProviderId), nameof(Loan.ExpressDispatchAtLocal) });
    }

    public async Task UpdatePickupPromptSentAsync(string id, DateTime sentAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(Loan.PickupPromptSentAt)] = FirestoreJson.TimestampField(sentAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Loan.PickupPromptSentAt) });
    }

    private static JsonObject ToFields(Loan loan)
    {
        return new JsonObject
        {
            [nameof(Loan.ItemCopyId)] = FirestoreJson.StringOrNullField(loan.ItemCopyId),
            [nameof(Loan.BorrowerId)] = FirestoreJson.StringOrNullField(loan.BorrowerId),
            [nameof(Loan.LoanDate)] = FirestoreJson.TimestampField(loan.LoanDate),
            [nameof(Loan.StartDate)] = FirestoreJson.TimestampField(loan.StartDate),
            [nameof(Loan.RentalDays)] = FirestoreJson.DoubleField(loan.RentalDays),
            [nameof(Loan.Quantity)] = FirestoreJson.DoubleField(loan.Units),
            [nameof(Loan.DailyRate)] = FirestoreJson.DoubleOrNullField(loan.DailyRate.HasValue ? (double)loan.DailyRate.Value : null),
            [nameof(Loan.IsLocation)] = FirestoreJson.BooleanField(loan.IsLocation),
            [nameof(Loan.StartTime)] = FirestoreJson.StringOrNullField(loan.StartTime),
            [nameof(Loan.CommissionRate)] = FirestoreJson.DoubleOrNullField(loan.CommissionRate.HasValue ? (double)loan.CommissionRate.Value : null),
            [nameof(Loan.ExpectedReturnDate)] = FirestoreJson.TimestampField(loan.ExpectedReturnDate),
            [nameof(Loan.ActualReturnDate)] = FirestoreJson.TimestampOrNullField(loan.ActualReturnDate),
            [nameof(Loan.Status)] = FirestoreJson.StringField(loan.Status.ToString()),
            [nameof(Loan.DeliveryMethod)] = FirestoreJson.StringField(loan.DeliveryMethod.ToString()),
            [nameof(Loan.ExpressDelivery)] = FirestoreJson.BooleanField(loan.ExpressDelivery),
            [nameof(Loan.PaymentMethod)] = FirestoreJson.StringField(loan.PaymentMethod.ToString()),
            [nameof(Loan.PaymentStatus)] = FirestoreJson.StringField(loan.PaymentStatus.ToString()),
            [nameof(Loan.PaymentTransactionId)] = FirestoreJson.StringOrNullField(loan.PaymentTransactionId),
            [nameof(Loan.DeliveryFee)] = FirestoreJson.DoubleField((double)loan.DeliveryFee),
            [nameof(Loan.DeliveryAddress)] = FirestoreJson.StringOrNullField(loan.DeliveryAddress),
            [nameof(Loan.ReturnPickupAddress)] = FirestoreJson.StringOrNullField(loan.ReturnPickupAddress),
            [nameof(Loan.TermsAcceptedAtUtc)] = FirestoreJson.TimestampOrNullField(loan.TermsAcceptedAtUtc),
            [nameof(Loan.TermsVersion)] = FirestoreJson.StringOrNullField(loan.TermsVersion),
            [nameof(Loan.PhoneConfirmedAtUtc)] = FirestoreJson.TimestampOrNullField(loan.PhoneConfirmedAtUtc),
            [nameof(Loan.ConfirmedPhone)] = FirestoreJson.StringOrNullField(loan.ConfirmedPhone),
            [nameof(Loan.ReturnFee)] = FirestoreJson.DoubleField((double)loan.ReturnFee),
            [nameof(Loan.ShippingChargeStatus)] = FirestoreJson.StringOrNullField(loan.ShippingChargeStatus),
            [nameof(Loan.ShippingPaymentId)] = FirestoreJson.StringOrNullField(loan.ShippingPaymentId),
            [nameof(Loan.ShippingChargedAmount)] = FirestoreJson.DoubleField((double)loan.ShippingChargedAmount),
            [nameof(Loan.OwnerPayoutPaidAtUtc)] = FirestoreJson.TimestampOrNullField(loan.OwnerPayoutPaidAtUtc),
            [nameof(Loan.OwnerPayoutAmount)] = FirestoreJson.DoubleField((double)loan.OwnerPayoutAmount),
            [nameof(Loan.OwnerPayoutProofId)] = FirestoreJson.StringOrNullField(loan.OwnerPayoutProofId),
            [nameof(Loan.CancelledAtUtc)] = FirestoreJson.TimestampOrNullField(loan.CancelledAtUtc),
            [nameof(Loan.CancelledBy)] = FirestoreJson.StringOrNullField(loan.CancelledBy),
            [nameof(Loan.ReturnMethod)] = FirestoreJson.StringField(loan.ReturnMethod.ToString()),
            [nameof(Loan.OutboundTrackingId)] = FirestoreJson.StringOrNullField(loan.OutboundTrackingId),
            [nameof(Loan.ReturnTrackingId)] = FirestoreJson.StringOrNullField(loan.ReturnTrackingId),
            [nameof(Loan.OutboundProviderId)] = FirestoreJson.StringOrNullField(loan.OutboundProviderId),
            [nameof(Loan.ExpressDispatchAtLocal)] = FirestoreJson.TimestampOrNullField(loan.ExpressDispatchAtLocal is null ? null : DateTime.SpecifyKind(loan.ExpressDispatchAtLocal.Value, DateTimeKind.Utc)),
            [nameof(Loan.ReturnProviderId)] = FirestoreJson.StringOrNullField(loan.ReturnProviderId),
            [nameof(Loan.ReturnReminderSentAt)] = FirestoreJson.TimestampOrNullField(loan.ReturnReminderSentAt),
            [nameof(Loan.LateDays)] = FirestoreJson.DoubleField(loan.LateDays),
            [nameof(Loan.LateFeeCharged)] = FirestoreJson.DoubleField((double)loan.LateFeeCharged),
            [nameof(Loan.ExtensionChargedTotal)] = FirestoreJson.DoubleField((double)loan.ExtensionChargedTotal),
            [nameof(Loan.PaymentProofId)] = FirestoreJson.StringOrNullField(loan.PaymentProofId),
            [nameof(Loan.OwnerActionToken)] = FirestoreJson.StringOrNullField(loan.OwnerActionToken),
            [nameof(Loan.PickedUpAt)] = FirestoreJson.TimestampOrNullField(loan.PickedUpAt),
            [nameof(Loan.PickedUpBy)] = FirestoreJson.StringOrNullField(loan.PickedUpBy),
            [nameof(Loan.PickupPromptSentAt)] = FirestoreJson.TimestampOrNullField(loan.PickupPromptSentAt),
            [nameof(Loan.ReturnPromptSentAt)] = FirestoreJson.TimestampOrNullField(loan.ReturnPromptSentAt),
            [nameof(Loan.NotReturnedReportedAt)] = FirestoreJson.TimestampOrNullField(loan.NotReturnedReportedAt),
            [nameof(Loan.ExtensionStatus)] = FirestoreJson.StringOrNullField(loan.ExtensionStatus),
            [nameof(Loan.ExtensionDays)] = FirestoreJson.DoubleField(loan.ExtensionDays),
            [nameof(Loan.ExtensionRequestedAt)] = FirestoreJson.TimestampOrNullField(loan.ExtensionRequestedAt),
            [nameof(Loan.ExtensionAttempts)] = FirestoreJson.DoubleField(loan.ExtensionAttempts),
            [nameof(Loan.OrderGroupId)] = FirestoreJson.StringOrNullField(loan.OrderGroupId)
        };
    }

    private static Loan ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;

        return new Loan
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            ItemCopyId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ItemCopyId)),
            BorrowerId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.BorrowerId)),
            LoanDate = FirestoreJson.GetTimestamp(fields, nameof(Loan.LoanDate)),
            StartDate = FirestoreJson.GetTimestamp(fields, nameof(Loan.StartDate)),
            RentalDays = GetPositiveInt(fields, nameof(Loan.RentalDays), 1),
            Quantity = GetPositiveInt(fields, nameof(Loan.Quantity), 1),
            DailyRate = FirestoreJson.GetDoubleOrNull(fields, nameof(Loan.DailyRate)) is double rate ? (decimal)rate : null,
            IsLocation = FirestoreJson.GetBool(fields, nameof(Loan.IsLocation)),
            StartTime = FirestoreJson.GetStringOrNull(fields, nameof(Loan.StartTime)),
            CommissionRate = FirestoreJson.GetDoubleOrNull(fields, nameof(Loan.CommissionRate)) is double commissionRate ? (decimal)commissionRate : null,
            ExpectedReturnDate = FirestoreJson.GetTimestamp(fields, nameof(Loan.ExpectedReturnDate)),
            ActualReturnDate = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.ActualReturnDate)),
            Status = FirestoreJson.GetEnum(fields, nameof(Loan.Status), LoanStatus.Active),
            DeliveryMethod = FirestoreJson.GetEnum(fields, nameof(Loan.DeliveryMethod), DeliveryMethod.SelfPickup),
            ExpressDelivery = FirestoreJson.GetBool(fields, nameof(Loan.ExpressDelivery)),
            PaymentMethod = FirestoreJson.GetEnum(fields, nameof(Loan.PaymentMethod), PaymentMethod.Cash),
            PaymentStatus = FirestoreJson.GetEnum(fields, nameof(Loan.PaymentStatus), PaymentStatus.Pending),
            PaymentTransactionId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.PaymentTransactionId)),
            DeliveryFee = (decimal)FirestoreJson.GetDouble(fields, nameof(Loan.DeliveryFee)),
            DeliveryAddress = FirestoreJson.GetStringOrNull(fields, nameof(Loan.DeliveryAddress)),
            ReturnPickupAddress = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ReturnPickupAddress)),
            TermsAcceptedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.TermsAcceptedAtUtc)),
            TermsVersion = FirestoreJson.GetStringOrNull(fields, nameof(Loan.TermsVersion)),
            PhoneConfirmedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.PhoneConfirmedAtUtc)),
            ConfirmedPhone = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ConfirmedPhone)),
            ReturnFee = (decimal)FirestoreJson.GetDouble(fields, nameof(Loan.ReturnFee)),
            ShippingChargeStatus = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ShippingChargeStatus)),
            ShippingPaymentId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ShippingPaymentId)),
            ShippingChargedAmount = (decimal)FirestoreJson.GetDouble(fields, nameof(Loan.ShippingChargedAmount)),
            OwnerPayoutPaidAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.OwnerPayoutPaidAtUtc)),
            OwnerPayoutAmount = (decimal)FirestoreJson.GetDouble(fields, nameof(Loan.OwnerPayoutAmount)),
            OwnerPayoutProofId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.OwnerPayoutProofId)),
            CancelledAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.CancelledAtUtc)),
            CancelledBy = FirestoreJson.GetStringOrNull(fields, nameof(Loan.CancelledBy)),
            ReturnMethod = FirestoreJson.GetEnum(fields, nameof(Loan.ReturnMethod), ReturnMethod.SelfDropoff),
            OutboundTrackingId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.OutboundTrackingId)),
            ReturnTrackingId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ReturnTrackingId)),
            OutboundProviderId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.OutboundProviderId)),
            ExpressDispatchAtLocal = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.ExpressDispatchAtLocal)) is { } dispatchAt ? DateTime.SpecifyKind(dispatchAt, DateTimeKind.Unspecified) : null,
            ReturnProviderId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ReturnProviderId)),
            ReturnReminderSentAt = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.ReturnReminderSentAt)),
            LateDays = (int)FirestoreJson.GetDouble(fields, nameof(Loan.LateDays)),
            LateFeeCharged = (decimal)FirestoreJson.GetDouble(fields, nameof(Loan.LateFeeCharged)),
            ExtensionChargedTotal = (decimal)FirestoreJson.GetDouble(fields, nameof(Loan.ExtensionChargedTotal)),
            PaymentProofId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.PaymentProofId)),
            OwnerActionToken = FirestoreJson.GetStringOrNull(fields, nameof(Loan.OwnerActionToken)),
            PickedUpAt = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.PickedUpAt)),
            PickedUpBy = FirestoreJson.GetStringOrNull(fields, nameof(Loan.PickedUpBy)),
            PickupPromptSentAt = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.PickupPromptSentAt)),
            ReturnPromptSentAt = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.ReturnPromptSentAt)),
            NotReturnedReportedAt = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.NotReturnedReportedAt)),
            ExtensionStatus = FirestoreJson.GetStringOrNull(fields, nameof(Loan.ExtensionStatus)),
            ExtensionDays = (int)FirestoreJson.GetDouble(fields, nameof(Loan.ExtensionDays)),
            ExtensionRequestedAt = FirestoreJson.GetTimestampOrNull(fields, nameof(Loan.ExtensionRequestedAt)),
            ExtensionAttempts = (int)FirestoreJson.GetDouble(fields, nameof(Loan.ExtensionAttempts)),
            OrderGroupId = FirestoreJson.GetStringOrNull(fields, nameof(Loan.OrderGroupId))
        };
    }

    // מגן מפני ערך 0 או שלילי (למשל בהשכרות ישנות שנוצרו לפני הוספת שדה זה) - חוזר לברירת המחדל.
    private static int GetPositiveInt(JsonObject? fields, string name, int defaultValue)
    {
        var value = (int)FirestoreJson.GetDouble(fields, name);
        return value > 0 ? value : defaultValue;
    }
}
