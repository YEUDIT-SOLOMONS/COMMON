namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// יומן השיחות האוטומטיות (אוסף CallLogs ב-Firestore)
public class CallLogRepository : ICallLogRepository
{
    private const string CollectionName = "CallLogs";
    private readonly FirebaseDbContext _firebase;

    public CallLogRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<IReadOnlyList<CallLog>> GetAllAsync()
    {
        var documents = await _firebase.ListDocumentsAsync(CollectionName);
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<CallLog>> GetByStatusAsync(string status)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(CallLog.Status), FirestoreJson.StringField(status)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<CallLog>> GetByLoanIdAsync(string loanId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(CallLog.LoanId), FirestoreJson.StringField(loanId)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<CallLog>> GetByUserIdAsync(string userId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(CallLog.ToUserId), FirestoreJson.StringField(userId)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<CallLog?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        return document is null ? null : ToEntity(document);
    }

    public async Task<CallLog> CreateAsync(CallLog log)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(log));
        log.Id = FirestoreJson.ExtractDocumentId(created);
        return log;
    }

    public async Task UpdateAsync(CallLog log)
    {
        if (string.IsNullOrEmpty(log.Id)) return;
        await _firebase.ReplaceDocumentAsync(CollectionName, log.Id, ToFields(log));
    }

    private static JsonObject ToFields(CallLog log)
    {
        return new JsonObject
        {
            [nameof(CallLog.Kind)] = FirestoreJson.StringField(log.Kind),
            [nameof(CallLog.LoanId)] = FirestoreJson.StringOrNullField(log.LoanId),
            [nameof(CallLog.ItemId)] = FirestoreJson.StringOrNullField(log.ItemId),
            [nameof(CallLog.ItemName)] = FirestoreJson.StringField(log.ItemName),
            [nameof(CallLog.ToUserId)] = FirestoreJson.StringOrNullField(log.ToUserId),
            [nameof(CallLog.ToName)] = FirestoreJson.StringField(log.ToName),
            [nameof(CallLog.ToPhone)] = FirestoreJson.StringField(log.ToPhone),
            [nameof(CallLog.InterestedEmail)] = FirestoreJson.StringOrNullField(log.InterestedEmail),
            [nameof(CallLog.CreatedAtUtc)] = FirestoreJson.TimestampField(log.CreatedAtUtc),
            [nameof(CallLog.Status)] = FirestoreJson.StringField(log.Status),
            [nameof(CallLog.ScheduledForLocal)] = FirestoreJson.TimestampOrNullField(log.ScheduledForLocal is null ? null : DateTime.SpecifyKind(log.ScheduledForLocal.Value, DateTimeKind.Utc)), // שעון קיר ישראלי - נשמר "כמו שהוא"
            [nameof(CallLog.PlacedAtUtc)] = FirestoreJson.TimestampOrNullField(log.PlacedAtUtc),
            [nameof(CallLog.Detail)] = FirestoreJson.StringOrNullField(log.Detail),
            [nameof(CallLog.Message)] = FirestoreJson.StringOrNullField(log.Message),
            [nameof(CallLog.TwilioSid)] = FirestoreJson.StringOrNullField(log.TwilioSid)
        };
    }

    private static CallLog ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;

        return new CallLog
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            Kind = FirestoreJson.GetString(fields, nameof(CallLog.Kind)),
            LoanId = FirestoreJson.GetStringOrNull(fields, nameof(CallLog.LoanId)),
            ItemId = FirestoreJson.GetStringOrNull(fields, nameof(CallLog.ItemId)),
            ItemName = FirestoreJson.GetString(fields, nameof(CallLog.ItemName)),
            ToUserId = FirestoreJson.GetStringOrNull(fields, nameof(CallLog.ToUserId)),
            ToName = FirestoreJson.GetString(fields, nameof(CallLog.ToName)),
            ToPhone = FirestoreJson.GetString(fields, nameof(CallLog.ToPhone)),
            InterestedEmail = FirestoreJson.GetStringOrNull(fields, nameof(CallLog.InterestedEmail)),
            CreatedAtUtc = FirestoreJson.GetTimestamp(fields, nameof(CallLog.CreatedAtUtc)),
            Status = FirestoreJson.GetString(fields, nameof(CallLog.Status)),
            ScheduledForLocal = FirestoreJson.GetTimestampOrNull(fields, nameof(CallLog.ScheduledForLocal)),
            PlacedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(CallLog.PlacedAtUtc)),
            Detail = FirestoreJson.GetStringOrNull(fields, nameof(CallLog.Detail)),
            Message = FirestoreJson.GetStringOrNull(fields, nameof(CallLog.Message)),
            TwilioSid = FirestoreJson.GetStringOrNull(fields, nameof(CallLog.TwilioSid))
        };
    }
}
