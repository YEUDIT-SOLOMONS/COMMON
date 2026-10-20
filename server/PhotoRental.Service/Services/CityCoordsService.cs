namespace PhotoRental.Service.Services;

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

// מיקום ערים לפי OpenStreetMap (Nominatim). כל עיר נשלפת פעם אחת ונשמרת במסד (אוסף CityCoords),
// כך שאין פנייה חוזרת לשירות. לפי מדיניות השירות: עד בקשה אחת בשנייה, עם זיהוי האתר
public class CityCoordsService : ICityCoordsService
{
    private static readonly ConcurrentDictionary<string, CityCoordsDto?> Memory = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private const int MaxLookupsPerRequest = 8;

    private readonly HttpClient _http;
    private readonly ICityCoordsRepository _repo;

    public CityCoordsService(HttpClient http, ICityCoordsRepository repo)
    {
        _http = http;
        _repo = repo;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("commonrent/1.0 (common@onyxapp.co.il)");
    }

    public async Task<IReadOnlyList<CityCoordsDto>> GetAsync(IEnumerable<string> cities)
    {
        var names = cities.Select(c => (c ?? string.Empty).Trim()).Where(c => c.Length is > 1 and < 60).Distinct().Take(200).ToList();
        var result = new List<CityCoordsDto>();
        var lookups = 0;

        foreach (var name in names)
        {
            if (Memory.TryGetValue(name, out var known))
            {
                if (known is not null) result.Add(known);
                continue;
            }

            var stored = await _repo.GetAsync(name);
            if (stored.Found)
            {
                Memory[name] = stored.Coords;
                if (stored.Coords is not null) result.Add(stored.Coords);
                continue;
            }

            if (lookups >= MaxLookupsPerRequest)
                continue;
            lookups++;
            var coords = await LookupAsync(name);
            Memory[name] = coords;
            try { await _repo.SaveAsync(name, coords); } catch { }
            if (coords is not null) result.Add(coords);
        }
        return result;
    }

    public async Task<CityCoordsDto?> GetStreetAsync(string city, string street, bool lookup)
    {
        city = city.Trim();
        street = street.Trim();
        if (city.Length < 2 || street.Length < 2)
            return null;
        var key = $"רחוב:{city}|{street}";
        if (Memory.TryGetValue(key, out var known))
            return known;
        var stored = await _repo.GetAsync(key);
        if (stored.Found)
            return Memory[key] = stored.Coords;
        if (!lookup)
            return null;

        CityCoordsDto? coords = null;
        await Gate.WaitAsync();
        try
        {
            foreach (var query in new[]
                     {
                         "street=" + Uri.EscapeDataString(street) + "&city=" + Uri.EscapeDataString(city),
                         "q=" + Uri.EscapeDataString($"{street}, {city}")
                     })
            {
                using var response = await _http.GetAsync(
                    "https://nominatim.openstreetmap.org/search?format=json&limit=1&accept-language=he&bounded=1&viewbox=34.2,33.4,35.95,29.4&" + query);
                await Task.Delay(1100);
                if (!response.IsSuccessStatusCode)
                    continue;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var first = doc.RootElement.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object
                    && double.TryParse(first.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(first.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                {
                    coords = new CityCoordsDto { City = city, Lat = lat, Lng = lng };
                    break;
                }
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }

        Memory[key] = coords;
        try { await _repo.SaveAsync(key, coords); } catch { }
        return coords;
    }

    private async Task<CityCoordsDto?> LookupAsync(string city)
    {
        await Gate.WaitAsync();
        try
        {
            // תחום חיפוש: ישראל כולל יהודה ושומרון (בלי הגבלת קוד מדינה, שמחריגה יישובים כמו מודיעין עילית וביתר עילית)
            var url = "https://nominatim.openstreetmap.org/search?format=json&limit=8&accept-language=he&bounded=1&viewbox=34.2,33.4,35.95,29.4&q="
                      + Uri.EscapeDataString(city);
            using var response = await _http.GetAsync(url);
            await Task.Delay(1100);
            if (!response.IsSuccessStatusCode)
                return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var results = doc.RootElement.EnumerateArray().ToList();
            // עדיפות ליישוב עצמו (עיר/עיירה/כפר) על פני רחוב או מקום אחר באותו שם
            var first = results.FirstOrDefault(r => r.TryGetProperty("addresstype", out var t)
                                                    && t.GetString() is "city" or "town" or "village" or "hamlet" or "suburb");
            if (first.ValueKind != JsonValueKind.Object)
                first = results.FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Object
                || !double.TryParse(first.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                || !double.TryParse(first.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
                return null;
            return new CityCoordsDto { City = city, Lat = lat, Lng = lng };
        }
        catch
        {
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
