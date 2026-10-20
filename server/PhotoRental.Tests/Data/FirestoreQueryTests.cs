using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Data;
using PhotoRental.Data.Migrations;
using PhotoRental.Data.Repositories;

namespace PhotoRental.Tests.Data;

public class FirestoreQueryTests
{
    private static FirebaseDbContext NewContext() =>
        new("photorental-test-" + Guid.NewGuid().ToString("N")[..8], new Uri($"http://{EmulatorFactAttribute.Host}"));

    [EmulatorFact]
    public async Task GetPage_ReturnsRequestedSliceAndTotal()
    {
        var db = NewContext();
        var repo = new ItemRepository(db);
        for (var i = 0; i < 7; i++)
            await repo.CreateAsync(new Item { Name = $"פריט {i}", DailyPrice = 10 + i, CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i) });

        var (page2, total) = await repo.GetPageAsync(new ItemPageQueryDto { Page = 2, PageSize = 3, Sort = "price" });

        Assert.Equal(7, total);
        Assert.Equal(new[] { 13m, 14m, 15m }, page2.Select(i => i.DailyPrice));
    }

    [EmulatorFact]
    public async Task MigrationRunner_RunsEachMigrationOnce()
    {
        var db = NewContext();
        await db.CreateDocumentAsync("Items", new JsonObject { ["Name"] = FirestoreJson.StringField("ישן") });
        var runner = new DataMigrationRunner(db,
            new IDataMigration[] { new M0001_BackfillItemQueryFields(), new M0002_AddItemVersion() },
            NullLogger<DataMigrationRunner>.Instance);

        var first = await runner.MigrateAsync();
        var second = await runner.MigrateAsync();

        Assert.Equal(2, first.Count);
        Assert.Empty(second);
        var (items, total) = await new ItemRepository(db).GetPageAsync(new ItemPageQueryDto());
        Assert.Equal(1, total);
        Assert.Equal(0, items[0].Version);
    }
}
