namespace PhotoRental.Data.Repositories;

using System.Text.Json;
using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

public class SaleRepository : ISaleRepository
{
    private const string CollectionName = "Sales";
    private readonly FirebaseDbContext _firebase;

    public SaleRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<Sale?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        return document is null ? null : ToEntity(document);
    }

    public async Task<IReadOnlyList<Sale>> GetAllAsync() =>
        (await _firebase.ListDocumentsAsync(CollectionName)).Select(ToEntity).ToList();

    public async Task<Sale> CreateAsync(Sale sale)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(sale));
        sale.Id = FirestoreJson.ExtractDocumentId(created);
        return sale;
    }

    public Task UpdateAsync(Sale sale) =>
        _firebase.ReplaceDocumentAsync(CollectionName, sale.Id!, ToFields(sale));

    private static JsonObject ToFields(Sale s) => new()
    {
        [nameof(Sale.ItemId)] = FirestoreJson.StringField(s.ItemId),
        [nameof(Sale.SellerId)] = FirestoreJson.StringField(s.SellerId),
        [nameof(Sale.BuyerId)] = FirestoreJson.StringField(s.BuyerId),
        [nameof(Sale.Status)] = FirestoreJson.StringField(s.Status),
        [nameof(Sale.PaymentTransactionId)] = FirestoreJson.StringField(s.PaymentTransactionId),
        [nameof(Sale.CreatedAtUtc)] = FirestoreJson.TimestampField(s.CreatedAtUtc),
        ["DataJson"] = FirestoreJson.StringField(JsonSerializer.Serialize(s))
    };

    private static Sale ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;
        Sale sale;
        try
        {
            sale = JsonSerializer.Deserialize<Sale>(FirestoreJson.GetString(fields, "DataJson")) ?? new Sale();
        }
        catch (JsonException)
        {
            sale = new Sale();
        }
        sale.Id = FirestoreJson.ExtractDocumentId(document);
        return sale;
    }
}
