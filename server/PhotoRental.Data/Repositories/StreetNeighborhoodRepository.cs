namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

public class StreetNeighborhoodRepository : IStreetNeighborhoodRepository
{
    private const string CollectionName = "StreetNeighborhoods";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(20);
    private static IReadOnlyDictionary<string, string>? _cache;
    private static DateTime _cachedAtUtc;

    private readonly FirebaseDbContext _firebase;

    public StreetNeighborhoodRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    private static string DocId(string key) => Uri.EscapeDataString(key).Replace("%", "_");

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync()
    {
        if (_cache is not null && DateTime.UtcNow - _cachedAtUtc < CacheFor)
            return _cache;

        var map = new Dictionary<string, string>();
        foreach (var doc in await _firebase.ListDocumentsAsync(CollectionName))
        {
            var fields = doc["fields"] as JsonObject;
            var name = FirestoreJson.GetString(fields, "Neighborhood");
            if (!string.IsNullOrWhiteSpace(name))
                map[KnownNeighborhoods.Key(FirestoreJson.GetString(fields, "City"), FirestoreJson.GetString(fields, "Street"))] = name;
        }
        _cache = map;
        _cachedAtUtc = DateTime.UtcNow;
        return map;
    }

    public async Task SetAsync(string city, string street, string? neighborhood)
    {
        var key = KnownNeighborhoods.Key(city, street);
        if (string.IsNullOrWhiteSpace(neighborhood))
            await _firebase.DeleteDocumentAsync(CollectionName, DocId(key));
        else
            await _firebase.ReplaceDocumentAsync(CollectionName, DocId(key), new JsonObject
            {
                ["City"] = FirestoreJson.StringField(city.Trim()),
                ["Street"] = FirestoreJson.StringField(street.Trim()),
                ["Neighborhood"] = FirestoreJson.StringField(neighborhood.Trim())
            });
        _cache = null;
    }
}
