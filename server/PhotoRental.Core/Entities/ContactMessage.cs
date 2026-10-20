namespace PhotoRental.Core.Entities;


// פנייה שנשלחה דרך דף "יצירת קשר" באתר
public class ContactMessage
{
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
