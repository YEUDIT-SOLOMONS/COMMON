namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// פרטי הזמנות שממתינות לתשלום באשראי (אוסף PaymentDrafts ב-Firestore)
public class PaymentDraftRepository : IPaymentDraftRepository
{
    private const string CollectionName = "PaymentDrafts";
    private readonly FirebaseDbContext _firebase;

    public PaymentDraftRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<PaymentDraft> CreateAsync(PaymentDraft draft)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, new JsonObject
        {
            [nameof(PaymentDraft.UserId)] = FirestoreJson.StringField(draft.UserId),
            [nameof(PaymentDraft.Draft)] = FirestoreJson.StringField(draft.Draft),
            [nameof(PaymentDraft.IsBatch)] = FirestoreJson.BooleanField(draft.IsBatch),
            [nameof(PaymentDraft.CreatedAtUtc)] = FirestoreJson.TimestampField(draft.CreatedAtUtc)
        });
        draft.Id = FirestoreJson.ExtractDocumentId(created);
        return draft;
    }

    public async Task<PaymentDraft?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        if (document is null) return null;

        var fields = document["fields"] as JsonObject;
        return new PaymentDraft
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            UserId = FirestoreJson.GetString(fields, nameof(PaymentDraft.UserId)),
            Draft = FirestoreJson.GetString(fields, nameof(PaymentDraft.Draft)),
            IsBatch = FirestoreJson.GetBool(fields, nameof(PaymentDraft.IsBatch)),
            CreatedAtUtc = FirestoreJson.GetTimestamp(fields, nameof(PaymentDraft.CreatedAtUtc))
        };
    }
}
