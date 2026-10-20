namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface ICityCoordsRepository
{
    // Found=false: העיר עוד לא נבדקה. Found=true ו-Coords=null: נבדקה ולא נמצאה
    Task<(bool Found, CityCoordsDto? Coords)> GetAsync(string city);
    Task SaveAsync(string city, CityCoordsDto? coords);
}
