namespace PhotoRental.Data.Repositories;

using System.Text.Json;
using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

public class ItemQuestionRepository : IItemQuestionRepository
{
    private const string CollectionName = "ItemQuestions";
    private readonly FirebaseDbContext _firebase;

    public ItemQuestionRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<ItemQuestion?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        return document is null ? null : ToEntity(document);
    }

    public async Task<IReadOnlyList<ItemQuestion>> GetAllAsync() =>
        (await _firebase.ListDocumentsAsync(CollectionName)).Select(ToEntity).ToList();

    public async Task<ItemQuestion> CreateAsync(ItemQuestion question)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(question));
        question.Id = FirestoreJson.ExtractDocumentId(created);
        return question;
    }

    public Task UpdateAsync(ItemQuestion question) =>
        _firebase.ReplaceDocumentAsync(CollectionName, question.Id!, ToFields(question));

    private const string ImagesCollection = "QuestionImages";

    public async Task<string> AddImageAsync(string threadId, string dataUrl)
    {
        var created = await _firebase.CreateDocumentAsync(ImagesCollection, new JsonObject
        {
            ["ThreadId"] = FirestoreJson.StringField(threadId),
            ["DataUrl"] = FirestoreJson.StringField(dataUrl),
            ["CreatedAtUtc"] = FirestoreJson.TimestampField(DateTime.UtcNow)
        });
        return FirestoreJson.ExtractDocumentId(created)!;
    }

    public async Task<(string ThreadId, string DataUrl)?> GetImageAsync(string imageId)
    {
        var doc = await _firebase.GetDocumentAsync(ImagesCollection, imageId);
        if (doc is null)
            return null;
        var fields = doc["fields"] as JsonObject;
        return (FirestoreJson.GetString(fields, "ThreadId"), FirestoreJson.GetString(fields, "DataUrl"));
    }

    private static JsonObject ToFields(ItemQuestion q) => new()
    {
        [nameof(ItemQuestion.ItemId)] = FirestoreJson.StringField(q.ItemId),
        [nameof(ItemQuestion.OwnerId)] = FirestoreJson.StringField(q.OwnerId),
        [nameof(ItemQuestion.RenterId)] = FirestoreJson.StringField(q.RenterId),
        [nameof(ItemQuestion.OwnerToken)] = FirestoreJson.StringField(q.OwnerToken),
        [nameof(ItemQuestion.RenterToken)] = FirestoreJson.StringField(q.RenterToken),
        ["MessagesJson"] = FirestoreJson.StringField(JsonSerializer.Serialize(q.Messages)),
        [nameof(ItemQuestion.CreatedAtUtc)] = FirestoreJson.TimestampField(q.CreatedAtUtc),
        [nameof(ItemQuestion.LastMessageAtUtc)] = FirestoreJson.TimestampField(q.LastMessageAtUtc),
        [nameof(ItemQuestion.OwnerReadAtUtc)] = FirestoreJson.TimestampOrNullField(q.OwnerReadAtUtc),
        [nameof(ItemQuestion.RenterReadAtUtc)] = FirestoreJson.TimestampOrNullField(q.RenterReadAtUtc)
    };

    private static ItemQuestion ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;
        List<ItemQuestionMessage> messages;
        try
        {
            messages = JsonSerializer.Deserialize<List<ItemQuestionMessage>>(FirestoreJson.GetString(fields, "MessagesJson")) ?? new();
        }
        catch (JsonException)
        {
            messages = new();
        }

        return new ItemQuestion
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            ItemId = FirestoreJson.GetString(fields, nameof(ItemQuestion.ItemId)),
            OwnerId = FirestoreJson.GetString(fields, nameof(ItemQuestion.OwnerId)),
            RenterId = FirestoreJson.GetString(fields, nameof(ItemQuestion.RenterId)),
            OwnerToken = FirestoreJson.GetString(fields, nameof(ItemQuestion.OwnerToken)),
            RenterToken = FirestoreJson.GetString(fields, nameof(ItemQuestion.RenterToken)),
            Messages = messages,
            CreatedAtUtc = FirestoreJson.GetTimestamp(fields, nameof(ItemQuestion.CreatedAtUtc)),
            LastMessageAtUtc = FirestoreJson.GetTimestamp(fields, nameof(ItemQuestion.LastMessageAtUtc)),
            OwnerReadAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(ItemQuestion.OwnerReadAtUtc)),
            RenterReadAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(ItemQuestion.RenterReadAtUtc))
        };
    }
}
