namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Interfaces;

public class MarkerRepository : IMarkerRepository
{
    private const string CollectionName = "Markers";
    private readonly FirebaseDbContext _firebase;

    public MarkerRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<bool> ExistsAsync(string key) =>
        await _firebase.GetDocumentAsync(CollectionName, key) is not null;

    public Task MarkAsync(string key) =>
        _firebase.ReplaceDocumentAsync(CollectionName, key, new JsonObject
        {
            ["AtUtc"] = FirestoreJson.TimestampField(DateTime.UtcNow)
        });
}
