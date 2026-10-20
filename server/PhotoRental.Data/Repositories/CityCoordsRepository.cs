namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

public class CityCoordsRepository : ICityCoordsRepository
{
    private const string CollectionName = "CityCoordsV2";
    private readonly FirebaseDbContext _firebase;

    public CityCoordsRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    private static string DocId(string city) => Uri.EscapeDataString(city).Replace("%", "_");

    public async Task<(bool Found, CityCoordsDto? Coords)> GetAsync(string city)
    {
        var doc = await _firebase.GetDocumentAsync(CollectionName, DocId(city));
        if (doc is null)
            return (false, null);
        var fields = doc["fields"] as JsonObject;
        if (!FirestoreJson.GetBool(fields, "Found"))
            return (true, null);
        return (true, new CityCoordsDto
        {
            City = city,
            Lat = FirestoreJson.GetDouble(fields, "Lat"),
            Lng = FirestoreJson.GetDouble(fields, "Lng")
        });
    }

    public Task SaveAsync(string city, CityCoordsDto? coords) =>
        _firebase.ReplaceDocumentAsync(CollectionName, DocId(city), new JsonObject
        {
            ["City"] = FirestoreJson.StringField(city),
            ["Found"] = FirestoreJson.BooleanField(coords is not null),
            ["Lat"] = FirestoreJson.DoubleField(coords?.Lat ?? 0),
            ["Lng"] = FirestoreJson.DoubleField(coords?.Lng ?? 0)
        });
}
