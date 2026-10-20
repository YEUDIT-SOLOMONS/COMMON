namespace PhotoRental.Core.Entities;

// פריט שמשתמשת שמרה במועדפים ו/או ביקשה התראה כשהוא מתפנה. מזהה המסמך: {UserId}_{ItemId}
public class Favorite
{
    public string Id => $"{UserId}_{ItemId}";
    public string UserId { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public bool NotifyWhenAvailable { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? NotifiedAtUtc { get; set; }
}
