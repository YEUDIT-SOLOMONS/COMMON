namespace PhotoRental.Core.Helpers;

using System.Collections.Concurrent;

// מטמון קצר בזיכרון לנתוני הקטלוג הציבוריים (פריטים, קטגוריות, "הכי מבוקשים"): כניסות חוזרות לא קוראות שוב מהמסד.
// כשפג התוקף מוחזר מיד הערך הקודם והרענון רץ ברקע (טעינה אחת משותפת לכל הבקשות). אחרי כתיבה (ראו FirebaseDbContext)
// ממתינים לערך החדש עד שנייה וחצי, ואם הוא מתעכב - מוחזר הקודם.
public static class CatalogCache
{
    private sealed class Entry
    {
        public object? Value;
        public DateTime At;
        public long Version = -1;
        public Task? Refresh;
    }

    private static readonly ConcurrentDictionary<string, Entry> Entries = new();
    private static readonly TimeSpan WaitAfterWrite = TimeSpan.FromSeconds(1.5);
    private static long _version;

    public static void Invalidate()
    {
        Interlocked.Increment(ref _version);
        foreach (var (key, entry) in Entries)
            if (entry.Refresh is null && DateTime.UtcNow - entry.At > TimeSpan.FromMinutes(10))
                Entries.TryRemove(key, out _);
    }

    public static async Task<T> GetOrAddAsync<T>(string key, TimeSpan ttl, Func<Task<T>> factory)
    {
        var entry = Entries.GetOrAdd(key, _ => new Entry());
        Task<T> refresh;
        T? stale;
        bool hasValue, invalidated;
        lock (entry)
        {
            var version = Interlocked.Read(ref _version);
            hasValue = entry.Value is not null;
            invalidated = entry.Version != version;
            if (hasValue && !invalidated && DateTime.UtcNow - entry.At < ttl)
                return (T)entry.Value!;
            if (entry.Refresh is not Task<T> running)
            {
                running = LoadAsync(entry, version, factory);
                entry.Refresh = running;
            }
            refresh = running;
            stale = hasValue ? (T)entry.Value! : default;
        }

        if (!hasValue)
            return await refresh;
        if (invalidated && await Task.WhenAny(refresh, Task.Delay(WaitAfterWrite)) == refresh && refresh.IsCompletedSuccessfully)
            return refresh.Result;
        return stale!;
    }

    private static async Task<T> LoadAsync<T>(Entry entry, long version, Func<Task<T>> factory)
    {
        await Task.Yield();
        try
        {
            var value = await factory();
            lock (entry)
            {
                entry.Value = value;
                entry.At = DateTime.UtcNow;
                entry.Version = version;
            }
            return value;
        }
        finally
        {
            lock (entry) entry.Refresh = null;
        }
    }
}
