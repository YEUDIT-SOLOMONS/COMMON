using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.Service.Services;

// מרחק (ק"מ) מכל פריט: לפי הרחוב של הפריט (לא הכתובת המדויקת). נקודת המוצא: המיקום מהדפדפן,
// אחרת הכתובת של המשתמשת המחוברת, אחרת מרכז העיר שנבחרה
public class ItemDistanceService : IItemDistanceService
{
    private const int MaxStreetLookups = 6;
    private readonly ICityCoordsService _coords;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;

    public ItemDistanceService(ICityCoordsService coords, IItemRepository items, IUserRepository users)
    {
        _coords = coords;
        _items = items;
        _users = users;
    }

    public async Task<ItemDistancesDto> GetAsync(double? lat, double? lng, string? city, string? userId)
    {
        CityCoordsDto? origin = null;
        var originKind = "none";
        if (lat is { } la && lng is { } ln && la is > 29 and < 34 && ln is > 34 and < 36.5)
        {
            origin = new CityCoordsDto { Lat = la, Lng = ln };
            originKind = "location";
        }
        if (origin is null && !string.IsNullOrEmpty(userId)
            && await _users.GetByIdAsync(userId) is { City: { Length: > 0 } userCity } user)
        {
            var street = user.AddressStreet ?? ItemService.StreetWithoutHouseNumberPublic(user.Address);
            if (!string.IsNullOrWhiteSpace(street))
                origin = await _coords.GetStreetAsync(userCity, street, lookup: true);
            originKind = origin is null ? originKind : "address";
            if (origin is null && string.IsNullOrWhiteSpace(city))
                city = userCity;
        }
        if (origin is null && !string.IsNullOrWhiteSpace(city))
        {
            origin = (await _coords.GetAsync(new[] { city })).FirstOrDefault();
            originKind = origin is null ? originKind : "city";
        }
        if (origin is null)
            return new ItemDistancesDto { Origin = originKind };

        var all = (await _items.GetAllAsync()).Where(i => i.DeletedAtUtc is null && !string.IsNullOrEmpty(i.Id) && !string.IsNullOrEmpty(i.City)).ToList();
        var cityPoints = (await _coords.GetAsync(all.Select(i => i.City!).Distinct())).ToDictionary(c => c.City, c => c);
        var result = new ItemDistancesDto { Origin = originKind };
        var lookups = 0;
        foreach (var item in all)
        {
            var street = ItemService.StreetOf(item);
            CityCoordsDto? point = null;
            if (!string.IsNullOrWhiteSpace(street))
            {
                point = await _coords.GetStreetAsync(item.City!, street, lookup: false);
                if (point is null && lookups < MaxStreetLookups)
                {
                    lookups++;
                    point = await _coords.GetStreetAsync(item.City!, street, lookup: true);
                }
            }
            point ??= cityPoints.GetValueOrDefault(item.City!);
            if (point is not null)
                result.Distances[item.Id!] = Math.Round(Km(origin, point), 1);
        }
        return result;
    }

    private static double Km(CityCoordsDto a, CityCoordsDto b)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(b.Lat - a.Lat);
        var dLng = Rad(b.Lng - a.Lng);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Lat)) * Math.Cos(Rad(b.Lat)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(h));
    }
}
