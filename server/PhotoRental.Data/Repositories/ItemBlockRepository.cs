namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

public class ItemBlockRepository : IItemBlockRepository
{
    private const string CollectionName = "ItemBlocks";
    private readonly FirebaseDbContext _firebase;

    public ItemBlockRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<IReadOnlyList<ItemBlock>> GetAllAsync() =>
        (await _firebase.ListDocumentsAsync(CollectionName)).Select(ToEntity).ToList();

    public async Task<IReadOnlyList<ItemBlock>> GetByItemAsync(string itemId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(ItemBlock.ItemId), FirestoreJson.StringField(itemId)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<ItemBlock?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        return document is null ? null : ToEntity(document);
    }

    public async Task<ItemBlock> CreateAsync(ItemBlock block)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, new JsonObject
        {
            [nameof(ItemBlock.ItemId)] = FirestoreJson.StringField(block.ItemId),
            [nameof(ItemBlock.OwnerId)] = FirestoreJson.StringField(block.OwnerId),
            [nameof(ItemBlock.From)] = FirestoreJson.StringField(block.From.ToString("yyyy-MM-dd")),
            [nameof(ItemBlock.To)] = FirestoreJson.StringField(block.To.ToString("yyyy-MM-dd")),
            [nameof(ItemBlock.Note)] = FirestoreJson.StringOrNullField(block.Note),
            [nameof(ItemBlock.CreatedAtUtc)] = FirestoreJson.TimestampField(block.CreatedAtUtc)
        });
        block.Id = FirestoreJson.ExtractDocumentId(created);
        return block;
    }

    public Task DeleteAsync(string id) => _firebase.DeleteDocumentAsync(CollectionName, id);

    private static ItemBlock ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;
        static DateTime Date(string value) =>
            DateTime.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
        return new ItemBlock
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            ItemId = FirestoreJson.GetString(fields, nameof(ItemBlock.ItemId)),
            OwnerId = FirestoreJson.GetString(fields, nameof(ItemBlock.OwnerId)),
            From = Date(FirestoreJson.GetString(fields, nameof(ItemBlock.From))),
            To = Date(FirestoreJson.GetString(fields, nameof(ItemBlock.To))),
            Note = FirestoreJson.GetStringOrNull(fields, nameof(ItemBlock.Note)),
            CreatedAtUtc = FirestoreJson.GetTimestamp(fields, nameof(ItemBlock.CreatedAtUtc))
        };
    }
}
