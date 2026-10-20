namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface ICityCoordsService
{
    Task<IReadOnlyList<CityCoordsDto>> GetAsync(IEnumerable<string> cities);

    // מיקום רחוב (בלי מספר בית) בעיר. lookup=false: רק ממה שכבר נשמר
    Task<CityCoordsDto?> GetStreetAsync(string city, string street, bool lookup);
}
