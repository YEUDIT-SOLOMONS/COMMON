namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

public class FavoriteRepository : IFavoriteRepository
{
    private const string CollectionName = "Favorites";
    private readonly FirebaseDbContext _firebase;

    public FavoriteRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<Favorite?> GetAsync(string userId, string itemId)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, $"{userId}_{itemId}");
        return document is null ? null : ToEntity(document);
    }

    public async Task<IReadOnlyList<Favorite>> GetByUserAsync(string userId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(Favorite.UserId), FirestoreJson.StringField(userId)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Favorite>> GetWatchersAsync()
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(Favorite.NotifyWhenAvailable), FirestoreJson.BooleanField(true)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Favorite>> GetAllAsync() =>
        (await _firebase.ListDocumentsAsync(CollectionName)).Select(ToEntity).ToList();

    public Task UpsertAsync(Favorite favorite) =>
        _firebase.ReplaceDocumentAsync(CollectionName, favorite.Id, new JsonObject
        {
            [nameof(Favorite.UserId)] = FirestoreJson.StringField(favorite.UserId),
            [nameof(Favorite.ItemId)] = FirestoreJson.StringField(favorite.ItemId),
            [nameof(Favorite.IsFavorite)] = FirestoreJson.BooleanField(favorite.IsFavorite),
            [nameof(Favorite.NotifyWhenAvailable)] = FirestoreJson.BooleanField(favorite.NotifyWhenAvailable),
            [nameof(Favorite.CreatedAtUtc)] = FirestoreJson.TimestampField(favorite.CreatedAtUtc),
            [nameof(Favorite.NotifiedAtUtc)] = FirestoreJson.TimestampOrNullField(favorite.NotifiedAtUtc)
        });

    public Task DeleteAsync(string userId, string itemId) =>
        _firebase.DeleteDocumentAsync(CollectionName, $"{userId}_{itemId}");

    private static Favorite ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;
        return new Favorite
        {
            UserId = FirestoreJson.GetString(fields, nameof(Favorite.UserId)),
            ItemId = FirestoreJson.GetString(fields, nameof(Favorite.ItemId)),
            IsFavorite = FirestoreJson.GetBool(fields, nameof(Favorite.IsFavorite)),
            NotifyWhenAvailable = FirestoreJson.GetBool(fields, nameof(Favorite.NotifyWhenAvailable)),
            CreatedAtUtc = FirestoreJson.GetTimestamp(fields, nameof(Favorite.CreatedAtUtc)),
            NotifiedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Favorite.NotifiedAtUtc))
        };
    }
}
