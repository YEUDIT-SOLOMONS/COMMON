namespace PhotoRental.Data.Migrations;

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

// ל-Firestore אין סכמה ולכן אין migrations של EF Core. המקבילה: שינויי נתונים ממוספרים שרצים
// בעליית השרת, כל אחד פעם אחת בלבד. מה שכבר רץ נרשם באוסף _migrations (כמו __EFMigrationsHistory)
public interface IDataMigration
{
    string Id { get; }
    string Description { get; }
    Task UpAsync(FirebaseDbContext db, CancellationToken ct);
}

public class DataMigrationRunner
{
    public const string HistoryCollection = "_migrations";

    private readonly FirebaseDbContext _db;
    private readonly IEnumerable<IDataMigration> _migrations;
    private readonly ILogger<DataMigrationRunner> _logger;

    public DataMigrationRunner(FirebaseDbContext db, IEnumerable<IDataMigration> migrations, ILogger<DataMigrationRunner> logger)
    {
        _db = db;
        _migrations = migrations;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken ct = default)
    {
        var applied = (await _db.ListDocumentsAsync(HistoryCollection, ct))
            .Select(FirestoreJson.ExtractDocumentId)
            .ToHashSet();

        var ran = new List<string>();
        foreach (var migration in _migrations.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            if (applied.Contains(migration.Id))
                continue;

            _logger.LogInformation("מריץ migration {MigrationId}: {Description}", migration.Id, migration.Description);
            await migration.UpAsync(_db, ct);

            await _db.CommitAsync(new[]
            {
                _db.CreateWrite(HistoryCollection, migration.Id, new JsonObject
                {
                    ["Description"] = FirestoreJson.StringField(migration.Description),
                    ["AppliedAtUtc"] = FirestoreJson.TimestampField(DateTime.UtcNow)
                })
            }, migration.Id, ct);
            ran.Add(migration.Id);
        }

        return ran;
    }
}

// שאילתות IS_NULL ב-Firestore מוצאות רק שדות שקיימים עם ערך null מפורש. פריטים ישנים בלי השדות
// האלה לא היו מופיעים בדפדוף - לכן משלימים אותם
public class M0001_BackfillItemQueryFields : IDataMigration
{
    public string Id => "0001_BackfillItemQueryFields";
    public string Description => "השלמת DeletedAtUtc / SoldAtUtc / CreatedAtUtc לפריטים קיימים";

    public async Task UpAsync(FirebaseDbContext db, CancellationToken ct)
    {
        foreach (var doc in await db.ListDocumentsAsync("Items", ct))
        {
            var fields = doc["fields"] as JsonObject;
            var patch = new JsonObject();
            if (fields?["DeletedAtUtc"] is null) patch["DeletedAtUtc"] = FirestoreJson.NullField();
            if (fields?["SoldAtUtc"] is null) patch["SoldAtUtc"] = FirestoreJson.NullField();
            if (fields?["CreatedAtUtc"]?["timestampValue"] is null)
                patch["CreatedAtUtc"] = FirestoreJson.TimestampField(DateTime.Parse(doc["createTime"]!.GetValue<string>()).ToUniversalTime());

            if (patch.Count > 0)
                await db.PatchDocumentFieldsAsync("Items", FirestoreJson.ExtractDocumentId(doc)!, patch, patch.Select(p => p.Key).ToList(), ct);
        }
    }
}

// concurrency token: כל פריט מקבל מונה גרסה התחלתי
public class M0002_AddItemVersion : IDataMigration
{
    public string Id => "0002_AddItemVersion";
    public string Description => "הוספת שדה Version לכל הפריטים (concurrency)";

    public async Task UpAsync(FirebaseDbContext db, CancellationToken ct)
    {
        foreach (var doc in await db.ListDocumentsAsync("Items", ct))
        {
            if ((doc["fields"] as JsonObject)?["Version"] is not null)
                continue;

            var patch = new JsonObject { ["Version"] = FirestoreJson.IntegerField(0) };
            await db.PatchDocumentFieldsAsync("Items", FirestoreJson.ExtractDocumentId(doc)!, patch, new[] { "Version" }, ct);
        }
    }
}
