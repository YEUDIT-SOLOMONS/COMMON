namespace PhotoRental.Core.Entities;

using Google.Cloud.Firestore;

// פנייה שנשלחה דרך דף "יצירת קשר" באתר
[FirestoreData]
public class ContactMessage
{
    [FirestoreDocumentId]
    public string? Id { get; set; }

    [FirestoreProperty]
    public string Name { get; set; } = string.Empty;

    [FirestoreProperty]
    public string Email { get; set; } = string.Empty;

    [FirestoreProperty]
    public string Message { get; set; } = string.Empty;

    [FirestoreProperty]
    public DateTime CreatedAtUtc { get; set; }
}
