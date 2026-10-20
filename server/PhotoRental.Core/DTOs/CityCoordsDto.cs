namespace PhotoRental.Core.DTOs;

// מיקום משוער של עיר (מרכז העיר) - למיון "הכי קרוב אליי" לפי מרחק אמיתי
public class CityCoordsDto
{
    public string City { get; set; } = string.Empty;
    public double Lat { get; set; }
    public double Lng { get; set; }
}
