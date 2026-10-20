namespace PhotoRental.Core.DTOs;

// מרחק (ק"מ) מכל פריט, ומאיפה נמדד: location (מיקום הדפדפן), address (הכתובת שלי), city, או none
public class ItemDistancesDto
{
    public string Origin { get; set; } = "none";
    public Dictionary<string, double> Distances { get; set; } = new();
}
