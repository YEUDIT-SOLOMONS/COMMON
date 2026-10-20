namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

public class AvailabilityRequestRepository : IAvailabilityRequestRepository
{
    private const string CollectionName = "AvailabilityRequests";
    private readonly FirebaseDbContext _firebase;

    public AvailabilityRequestRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<AvailabilityRequest?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        return document is null ? null : ToEntity(document);
    }

    public async Task<AvailabilityRequest> CreateAsync(AvailabilityRequest request)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(request));
        request.Id = FirestoreJson.ExtractDocumentId(created);
        return request;
    }

    public async Task<IReadOnlyList<AvailabilityRequest>> GetByRenterAsync(string renterId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(AvailabilityRequest.RenterId), FirestoreJson.StringField(renterId)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<AvailabilityRequest>> GetAnsweredAvailableAsync()
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(AvailabilityRequest.Answer), FirestoreJson.StringField("Available")) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<AvailabilityRequest>> GetByItemAsync(string itemId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(AvailabilityRequest.ItemId), FirestoreJson.StringField(itemId)) });
        return documents.Select(ToEntity).ToList();
    }

    public Task UpdateAsync(AvailabilityRequest request) =>
        _firebase.ReplaceDocumentAsync(CollectionName, request.Id!, ToFields(request));

    private static JsonObject ToFields(AvailabilityRequest r) => new()
    {
        [nameof(AvailabilityRequest.ItemId)] = FirestoreJson.StringField(r.ItemId),
        [nameof(AvailabilityRequest.OwnerId)] = FirestoreJson.StringField(r.OwnerId),
        [nameof(AvailabilityRequest.RenterId)] = FirestoreJson.StringField(r.RenterId),
        [nameof(AvailabilityRequest.StartDate)] = FirestoreJson.StringOrNullField(r.StartDate),
        [nameof(AvailabilityRequest.ReturnDate)] = FirestoreJson.StringOrNullField(r.ReturnDate),
        [nameof(AvailabilityRequest.RequestJson)] = FirestoreJson.StringField(r.RequestJson),
        [nameof(AvailabilityRequest.Token)] = FirestoreJson.StringField(r.Token),
        [nameof(AvailabilityRequest.Notify)] = FirestoreJson.StringField(r.Notify),
        [nameof(AvailabilityRequest.Answer)] = FirestoreJson.StringOrNullField(r.Answer),
        [nameof(AvailabilityRequest.AnsweredVia)] = FirestoreJson.StringOrNullField(r.AnsweredVia),
        [nameof(AvailabilityRequest.AnsweredAtUtc)] = FirestoreJson.TimestampOrNullField(r.AnsweredAtUtc),
        [nameof(AvailabilityRequest.Canceled)] = FirestoreJson.BooleanField(r.Canceled),
        [nameof(AvailabilityRequest.OwnerEmailed)] = FirestoreJson.BooleanField(r.OwnerEmailed),
        [nameof(AvailabilityRequest.RetryLogId)] = FirestoreJson.StringOrNullField(r.RetryLogId),
        [nameof(AvailabilityRequest.RetryAtLocal)] = FirestoreJson.StringOrNullField(r.RetryAtLocal),
        [nameof(AvailabilityRequest.CreatedAtUtc)] = FirestoreJson.TimestampField(r.CreatedAtUtc),
        [nameof(AvailabilityRequest.OrderReminderSentAtUtc)] = FirestoreJson.TimestampOrNullField(r.OrderReminderSentAtUtc)
    };

    private static AvailabilityRequest ToEntity(JsonObject document)
    {
        var f = document["fields"] as JsonObject;
        return new AvailabilityRequest
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            ItemId = FirestoreJson.GetString(f, nameof(AvailabilityRequest.ItemId)),
            OwnerId = FirestoreJson.GetString(f, nameof(AvailabilityRequest.OwnerId)),
            RenterId = FirestoreJson.GetString(f, nameof(AvailabilityRequest.RenterId)),
            StartDate = FirestoreJson.GetStringOrNull(f, nameof(AvailabilityRequest.StartDate)),
            ReturnDate = FirestoreJson.GetStringOrNull(f, nameof(AvailabilityRequest.ReturnDate)),
            RequestJson = FirestoreJson.GetString(f, nameof(AvailabilityRequest.RequestJson)),
            Token = FirestoreJson.GetString(f, nameof(AvailabilityRequest.Token)),
            Notify = FirestoreJson.GetStringOrNull(f, nameof(AvailabilityRequest.Notify)) ?? "email",
            Answer = FirestoreJson.GetStringOrNull(f, nameof(AvailabilityRequest.Answer)),
            AnsweredVia = FirestoreJson.GetStringOrNull(f, nameof(AvailabilityRequest.AnsweredVia)),
            AnsweredAtUtc = FirestoreJson.GetTimestampOrNull(f, nameof(AvailabilityRequest.AnsweredAtUtc)),
            Canceled = FirestoreJson.GetBool(f, nameof(AvailabilityRequest.Canceled)),
            OwnerEmailed = FirestoreJson.GetBool(f, nameof(AvailabilityRequest.OwnerEmailed)),
            RetryLogId = FirestoreJson.GetStringOrNull(f, nameof(AvailabilityRequest.RetryLogId)),
            RetryAtLocal = FirestoreJson.GetStringOrNull(f, nameof(AvailabilityRequest.RetryAtLocal)),
            CreatedAtUtc = FirestoreJson.GetTimestamp(f, nameof(AvailabilityRequest.CreatedAtUtc)),
            OrderReminderSentAtUtc = FirestoreJson.GetTimestampOrNull(f, nameof(AvailabilityRequest.OrderReminderSentAtUtc))
        };
    }
}
