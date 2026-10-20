using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Exceptions;
using PhotoRental.Data;
using PhotoRental.Data.Repositories;

namespace PhotoRental.Tests.Data;

// ההוכחה שהמנגנון עובד: שני חיבורים נפרדים (המקבילה של שני DbContext) קוראים את אותו פריט,
// שניהם מנסים להשכיר אותו - הראשון מצליח והשני נדחה
public class FirestoreConcurrencyTests
{
    private const string ProjectId = "photorental-test";

    private static FirebaseDbContext NewContext() =>
        new(ProjectId, new Uri($"http://{EmulatorFactAttribute.Host}"));

    private static async Task<string> SeedItemAsync(FirebaseDbContext db)
    {
        var created = await db.CreateDocumentAsync("Items", new JsonObject
        {
            [nameof(Item.Name)] = FirestoreJson.StringField("מצלמה לבדיקה"),
            [nameof(Item.Quantity)] = FirestoreJson.DoubleField(1),
            [nameof(Item.Version)] = FirestoreJson.IntegerField(0)
        });
        return FirestoreJson.ExtractDocumentId(created)!;
    }

    private static Loan NewLoan(string itemId, string borrowerId) => new()
    {
        ItemCopyId = itemId,
        BorrowerId = borrowerId,
        StartDate = DateTime.UtcNow.Date.AddDays(3),
        RentalDays = 2,
        ExpectedReturnDate = DateTime.UtcNow.Date.AddDays(5),
        LoanDate = DateTime.UtcNow
    };

    [EmulatorFact]
    public async Task TwoContexts_BookSameItem_SecondGetsConflict()
    {
        var first = NewContext();
        var second = NewContext();
        var itemId = await SeedItemAsync(first);

        var itemSeenByFirst = (await new ItemRepository(first).GetByIdAsync(itemId))!;
        var itemSeenBySecond = (await new ItemRepository(second).GetByIdAsync(itemId))!;
        Assert.Equal(itemSeenByFirst.ConcurrencyToken, itemSeenBySecond.ConcurrencyToken);

        var saved = await new LoanRepository(first).CreateWithItemCheckAsync(NewLoan(itemId, "renter-a"), itemSeenByFirst);
        Assert.NotNull(saved.Id);

        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new LoanRepository(second).CreateWithItemCheckAsync(NewLoan(itemId, "renter-b"), itemSeenBySecond));
        Assert.Equal(itemId, ex.ResourceId);

        var loans = await new LoanRepository(first).GetActiveLoansForItemAsync(itemId);
        Assert.Single(loans);
        Assert.Equal("renter-a", loans[0].BorrowerId);

        var itemAfter = (await new ItemRepository(first).GetByIdAsync(itemId))!;
        Assert.Equal(1, itemAfter.Version);
    }

    [EmulatorFact]
    public async Task AfterRereading_SecondBookingAttemptUsesFreshToken()
    {
        var db = NewContext();
        var itemId = await SeedItemAsync(db);
        var repo = new ItemRepository(db);

        var stale = (await repo.GetByIdAsync(itemId))!;
        await new LoanRepository(db).CreateWithItemCheckAsync(NewLoan(itemId, "renter-a"), stale);

        var fresh = (await repo.GetByIdAsync(itemId))!;
        Assert.NotEqual(stale.ConcurrencyToken, fresh.ConcurrencyToken);

        await new LoanRepository(db).CreateWithItemCheckAsync(NewLoan(itemId, "renter-b"), fresh);
        Assert.Equal(2, (await repo.GetByIdAsync(itemId))!.Version);
    }
}
