using PhotoRental.Core.DTOs;

namespace PhotoRental.Core.Interfaces;

public interface IItemDistanceService
{
    Task<ItemDistancesDto> GetAsync(double? lat, double? lng, string? city, string? userId);
}
