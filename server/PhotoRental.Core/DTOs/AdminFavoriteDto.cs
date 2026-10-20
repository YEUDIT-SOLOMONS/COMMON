namespace PhotoRental.Core.DTOs;

// פריט אהוב - כמה שמרו אותו במועדפים וכמה ממתינות שיתפנה
public class AdminFavoriteDto
{
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Favorites { get; set; }
    public int Waiting { get; set; }
    public DateTime? LastSavedUtc { get; set; }

    // מי שמרה (שם ומייל) - ומי ממתינה שיתפנה
    public List<AdminFavoriteSaverDto> Savers { get; set; } = new();
}

public class AdminFavoriteSaverDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool Waiting { get; set; }
}
