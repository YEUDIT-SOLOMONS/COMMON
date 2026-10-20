namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

public class ReviewRepository : IReviewRepository
{
    private const string CollectionName = "Reviews";
    private readonly FirebaseDbContext _firebase;

    public ReviewRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<IReadOnlyList<Review>> GetAllAsync() =>
        (await _firebase.ListDocumentsAsync(CollectionName)).Select(ToEntity).ToList();

    public async Task<IReadOnlyList<Review>> GetByItemAsync(string itemId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(Review.ItemId), FirestoreJson.StringField(itemId)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Review>> GetByOwnerAsync(string ownerId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName, new (string, JsonNode)[] { (nameof(Review.OwnerId), FirestoreJson.StringField(ownerId)) });
        return documents.Select(ToEntity).ToList();
    }

    public async Task<Review?> GetAsync(string loanId, string byRole)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, $"{loanId}_{byRole}");
        return document is null ? null : ToEntity(document);
    }

    public Task UpsertAsync(Review review) =>
        _firebase.ReplaceDocumentAsync(CollectionName, review.Id, new JsonObject
        {
            [nameof(Review.LoanId)] = FirestoreJson.StringField(review.LoanId),
            [nameof(Review.ByRole)] = FirestoreJson.StringField(review.ByRole),
            [nameof(Review.ItemId)] = FirestoreJson.StringField(review.ItemId),
            [nameof(Review.ItemName)] = FirestoreJson.StringField(review.ItemName),
            [nameof(Review.OwnerId)] = FirestoreJson.StringField(review.OwnerId),
            [nameof(Review.RenterId)] = FirestoreJson.StringField(review.RenterId),
            [nameof(Review.AuthorName)] = FirestoreJson.StringField(review.AuthorName),
            [nameof(Review.Stars)] = FirestoreJson.DoubleField(review.Stars),
            [nameof(Review.Text)] = FirestoreJson.StringOrNullField(review.Text),
            [nameof(Review.ThankYouNote)] = FirestoreJson.StringOrNullField(review.ThankYouNote),
            [nameof(Review.CreatedAtUtc)] = FirestoreJson.TimestampField(review.CreatedAtUtc),
            [nameof(Review.Hidden)] = FirestoreJson.BooleanField(review.Hidden)
        });

    public Task SetHiddenAsync(string id, bool hidden) =>
        _firebase.PatchDocumentFieldsAsync(
            CollectionName, id,
            new JsonObject { [nameof(Review.Hidden)] = FirestoreJson.BooleanField(hidden) },
            new[] { nameof(Review.Hidden) });

    private static Review ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;
        return new Review
        {
            LoanId = FirestoreJson.GetString(fields, nameof(Review.LoanId)),
            ByRole = FirestoreJson.GetString(fields, nameof(Review.ByRole)),
            ItemId = FirestoreJson.GetString(fields, nameof(Review.ItemId)),
            ItemName = FirestoreJson.GetString(fields, nameof(Review.ItemName)),
            OwnerId = FirestoreJson.GetString(fields, nameof(Review.OwnerId)),
            RenterId = FirestoreJson.GetString(fields, nameof(Review.RenterId)),
            AuthorName = FirestoreJson.GetString(fields, nameof(Review.AuthorName)),
            Stars = (int)Math.Round(FirestoreJson.GetDouble(fields, nameof(Review.Stars))),
            Text = FirestoreJson.GetStringOrNull(fields, nameof(Review.Text)),
            ThankYouNote = FirestoreJson.GetStringOrNull(fields, nameof(Review.ThankYouNote)),
            CreatedAtUtc = FirestoreJson.GetTimestamp(fields, nameof(Review.CreatedAtUtc)),
            Hidden = FirestoreJson.GetBool(fields, nameof(Review.Hidden))
        };
    }
}
