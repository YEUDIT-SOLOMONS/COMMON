namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// מימוש שכבת הגישה לנתונים עבור פניות "יצירת קשר", מול Firestore REST API
public class ContactMessageRepository : IContactMessageRepository
{
    private const string CollectionName = "ContactMessages";
    private readonly FirebaseDbContext _firebase;

    public ContactMessageRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<ContactMessage> CreateAsync(ContactMessage message)
    {
        var fields = new JsonObject
        {
            [nameof(ContactMessage.Name)] = FirestoreJson.StringField(message.Name),
            [nameof(ContactMessage.Email)] = FirestoreJson.StringField(message.Email),
            [nameof(ContactMessage.Message)] = FirestoreJson.StringField(message.Message),
            [nameof(ContactMessage.CreatedAtUtc)] = FirestoreJson.TimestampField(message.CreatedAtUtc)
        };

        var created = await _firebase.CreateDocumentAsync(CollectionName, fields);
        message.Id = FirestoreJson.ExtractDocumentId(created);
        return message;
    }
}
