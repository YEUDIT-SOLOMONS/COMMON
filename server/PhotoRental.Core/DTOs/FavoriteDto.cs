namespace PhotoRental.Core.DTOs;

public class FavoriteDto
{
    public string ItemId { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }
    public bool NotifyWhenAvailable { get; set; }

    // הפריט עצמו (ברשימה באזור האישי). null אם הפריט נמחק
    public ItemDto? Item { get; set; }
}

// null = לא לשנות את השדה
public class FavoriteUpdateDto
{
    public bool? IsFavorite { get; set; }
    public bool? NotifyWhenAvailable { get; set; }
}
