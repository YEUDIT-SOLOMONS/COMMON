namespace PhotoRental.Service.Services;

using System.Text;
using System.Text.Json;
using PhotoRental.Core.Interfaces;

// מדריך הכתובות מ-data.gov.il (CKAN datastore): מאגר "רחובות בישראל" (resource 9ad3862c-8391-4b2f-84a4-2d4c68625f4b,
// כ-63 אלף רחובות לפי יישוב: שדות שם_ישוב ו-שם_רחוב). נטען פעם אחת לזיכרון (שני עמודים של 32 אלף רשומות, כ-8MB) ונשמר
// 24 שעות; רשימת היישובים נגזרת ממנו, כך ששם היישוב תמיד תואם לרשימת הרחובות שלו.
public class AddressDirectory : IAddressDirectory
{
    private const string StreetsUrl = "https://data.gov.il/api/3/action/datastore_search?resource_id=9ad3862c-8391-4b2f-84a4-2d4c68625f4b&limit=32000&offset=";
    private const int PageSize = 32000;
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(24);

    private static readonly SemaphoreSlim LoadLock = new(1, 1);
    private static DateTime _loadedAtUtc = DateTime.MinValue;
    private static List<string> _cities = new();
    private static Dictionary<string, List<string>> _streetsByCity = new();

    private readonly HttpClient _http;

    public AddressDirectory(HttpClient http)
    {
        _http = http;
        _http.Timeout = TimeSpan.FromSeconds(90);
    }

    public async Task<IReadOnlyList<string>> SearchCitiesAsync(string query, int limit = 12)
    {
        await EnsureLoadedAsync();
        return Match(_cities, query, limit);
    }

    public async Task<IReadOnlyList<string>> SearchStreetsAsync(string city, string query, int limit = 12)
    {
        await EnsureLoadedAsync();
        return _streetsByCity.TryGetValue(Normalize(city), out var streets) ? Match(streets, query, limit) : Array.Empty<string>();
    }

    // התאמה לפי טקסט מנורמל: קודם שמות שמתחילים בטקסט, ואז שמות שמכילים אותו
    private static IReadOnlyList<string> Match(List<string> names, string query, int limit)
    {
        var q = Normalize(query);
        if (q.Length == 0) return Array.Empty<string>();

        var starts = new List<string>();
        var contains = new List<string>();
        foreach (var name in names)
        {
            var n = Normalize(name);
            if (n.StartsWith(q, StringComparison.Ordinal)) starts.Add(name);
            else if (n.Contains(q, StringComparison.Ordinal)) contains.Add(name);
            if (starts.Count >= limit) break;
        }

        if (starts.Count + contains.Count == 0)
            return MatchByTokens(names, q, limit);

        return starts.Concat(contains).Take(limit).ToList();
    }

    // "ד"ר נתן בירנבוים" או "רחוב בירנבוים" מול השם הרשמי "בירנבוים": שם הרחוב הרשמי מוכל במילים שהוקלדו
    private static IReadOnlyList<string> MatchByTokens(List<string> names, string normalizedQuery, int limit)
    {
        var queryTokens = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var name in names)
        {
            var tokens = Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 0 && tokens.All(queryTokens.Contains))
            {
                result.Add(name);
                if (result.Count >= limit) break;
            }
        }
        return result;
    }

    // מוריד גרשיים/מקפים/רווחים מיותרים כדי ש"תל אביב יפו" ו"תל אביב -יפו" ייחשבו אותו דבר
    internal static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is '"' or '\'' or '׳' or '״' or '`') continue;
            sb.Append(ch is '-' or '–' or '־' ? ' ' : char.ToLowerInvariant(ch));
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private async Task EnsureLoadedAsync()
    {
        if (DateTime.UtcNow - _loadedAtUtc < CacheFor && _cities.Count > 0)
            return;

        await LoadLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _loadedAtUtc < CacheFor && _cities.Count > 0)
                return;

            var byCity = new Dictionary<string, List<string>>();
            var cityNames = new Dictionary<string, string>();
            var offset = 0;
            var total = int.MaxValue;
            while (offset < total)
            {
                using var response = await _http.GetAsync(StreetsUrl + offset);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("מאגר הכתובות הממשלתי אינו זמין כרגע");

                using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                var result = doc.RootElement.GetProperty("result");
                total = result.GetProperty("total").GetInt32();
                var records = result.GetProperty("records");
                var count = 0;
                foreach (var record in records.EnumerateArray())
                {
                    count++;
                    var city = record.GetProperty("שם_ישוב").GetString()?.Trim();
                    var street = record.GetProperty("שם_רחוב").GetString()?.Trim();
                    if (string.IsNullOrEmpty(city) || string.IsNullOrEmpty(street)) continue;

                    var key = Normalize(city);
                    if (!byCity.TryGetValue(key, out var list))
                    {
                        byCity[key] = list = new List<string>();
                        cityNames[key] = city;
                    }
                    if (!list.Contains(street)) list.Add(street);
                }

                if (count == 0) break;
                offset += PageSize;
            }

            if (cityNames.Count == 0)
                throw new InvalidOperationException("מאגר הכתובות הממשלתי החזיר רשימה ריקה");

            foreach (var list in byCity.Values) list.Sort(StringComparer.Create(new System.Globalization.CultureInfo("he-IL"), false));
            _cities = cityNames.Values.OrderBy(c => c, StringComparer.Create(new System.Globalization.CultureInfo("he-IL"), false)).ToList();
            _streetsByCity = byCity;
            _loadedAtUtc = DateTime.UtcNow;
        }
        finally
        {
            LoadLock.Release();
        }
    }
}
