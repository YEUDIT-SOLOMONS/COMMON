namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

public class VisitRepository : IVisitRepository
{
    private const string CollectionName = "Visits";
    private readonly FirebaseDbContext _firebase;

    public VisitRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public Task RecordAsync(string day, bool newVisitor) =>
        _firebase.IncrementAsync(CollectionName, day, newVisitor
            ? new Dictionary<string, long> { ["Visits"] = 1, ["NewVisitors"] = 1 }
            : new Dictionary<string, long> { ["Visits"] = 1 });

    public async Task<IReadOnlyList<VisitDayDto>> GetAllAsync()
    {
        static long Num(JsonObject? fields, string name)
        {
            var node = fields?[name] as JsonObject;
            var raw = node?["integerValue"]?.ToString() ?? node?["doubleValue"]?.ToString();
            return long.TryParse(raw, out var v) ? v : double.TryParse(raw, out var d) ? (long)d : 0;
        }

        return (await _firebase.ListDocumentsAsync(CollectionName))
            .Select(doc => new VisitDayDto
            {
                Day = FirestoreJson.ExtractDocumentId(doc) ?? string.Empty,
                Visits = Num(doc["fields"] as JsonObject, "Visits"),
                NewVisitors = Num(doc["fields"] as JsonObject, "NewVisitors")
            })
            .OrderBy(d => d.Day)
            .ToList();
    }
}
