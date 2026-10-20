namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// אסמכתאות העברה בנקאית (אוסף PaymentProofs ב-Firestore)
public class PaymentProofRepository : IPaymentProofRepository
{
    private const string CollectionName = "PaymentProofs";
    private readonly FirebaseDbContext _firebase;

    public PaymentProofRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<PaymentProof> CreateAsync(PaymentProof proof)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, new JsonObject
        {
            [nameof(PaymentProof.FileName)] = FirestoreJson.StringField(proof.FileName),
            [nameof(PaymentProof.ContentType)] = FirestoreJson.StringField(proof.ContentType),
            [nameof(PaymentProof.DataUrl)] = FirestoreJson.StringField(proof.DataUrl),
            [nameof(PaymentProof.UploadedAtUtc)] = FirestoreJson.TimestampField(proof.UploadedAtUtc)
        });
        proof.Id = FirestoreJson.ExtractDocumentId(created);
        return proof;
    }

    public async Task<PaymentProof?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        if (document is null) return null;

        var fields = document["fields"] as JsonObject;
        return new PaymentProof
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            FileName = FirestoreJson.GetString(fields, nameof(PaymentProof.FileName)),
            ContentType = FirestoreJson.GetString(fields, nameof(PaymentProof.ContentType)),
            DataUrl = FirestoreJson.GetString(fields, nameof(PaymentProof.DataUrl)),
            UploadedAtUtc = FirestoreJson.GetTimestamp(fields, nameof(PaymentProof.UploadedAtUtc))
        };
    }
}
