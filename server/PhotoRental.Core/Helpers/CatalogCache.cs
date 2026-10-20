namespace PhotoRental.Core.Helpers;

using System.Collections.Concurrent;

// מטמון קצר בזיכרון לנתוני הקטלוג הציבוריים (פריטים, קטגוריות, "הכי מבוקשים"): כניסות חוזרות לא קוראות שוב מהמסד.
// כל כתיבה לאוספים שמשפיעים על הקטלוג מנקה את המטמון (ראו FirebaseDbContext), וגם פג תוקפו מעצמו אחרי TTL קצר.
public static class CatalogCache
{
    private static readonly ConcurrentDictionary<string, (DateTime At, object Value)> Entries = new();
    private static long _version;

    public static void Invalidate()
    {
        Interlocked.Increment(ref _version);
        Entries.Clear();
    }

    public static async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        if (Entries.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < ttl)
            return (T)hit.Value;

        var versionBefore = Interlocked.Read(ref _version);
        var value = await factory();
        if (versionBefore == Interlocked.Read(ref _version))
            Entries[key] = (DateTime.UtcNow, value!);
        return value;
    }
}
