namespace PhotoRental.Core.Security;

using System.Collections.Concurrent;

// חסימה זמנית של חשבון אחרי כמה ניסיונות התחברות כושלים ברצף (הגנה מפני ניחוש סיסמה).
// הספירה בזיכרון השרת: אינסטנציה נוספת סופרת בנפרד, ולכן זו שכבה נוספת ליד ההגבלה לפי כתובת IP
public class LoginThrottle
{
    private sealed class Entry
    {
        public int Failures;
        public DateTime WindowStartUtc;
        public DateTime? LockedUntilUtc;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly int _maxFailures;
    private readonly TimeSpan _window;
    private readonly TimeSpan _lockDuration;
    private readonly Func<DateTime> _utcNow;

    public LoginThrottle(int maxFailures = 5, TimeSpan? window = null, TimeSpan? lockDuration = null, Func<DateTime>? utcNow = null)
    {
        _maxFailures = maxFailures;
        _window = window ?? TimeSpan.FromMinutes(15);
        _lockDuration = lockDuration ?? TimeSpan.FromMinutes(15);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    // true אם החשבון חסום כרגע; remaining = כמה זמן נשאר
    public bool IsLocked(string email, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;
        if (!_entries.TryGetValue(Key(email), out var entry))
            return false;

        lock (entry)
        {
            if (entry.LockedUntilUtc is { } until && until > _utcNow())
            {
                remaining = until - _utcNow();
                return true;
            }
        }

        return false;
    }

    public void RecordFailure(string email)
    {
        var now = _utcNow();
        if (_entries.Count > 5000)
            Cleanup(now);

        var entry = _entries.GetOrAdd(Key(email), _ => new Entry { WindowStartUtc = now });
        lock (entry)
        {
            if (entry.LockedUntilUtc is { } until && until <= now)
            {
                entry.LockedUntilUtc = null;
                entry.Failures = 0;
                entry.WindowStartUtc = now;
            }

            if (now - entry.WindowStartUtc > _window)
            {
                entry.Failures = 0;
                entry.WindowStartUtc = now;
            }

            entry.Failures++;
            if (entry.Failures >= _maxFailures)
                entry.LockedUntilUtc = now + _lockDuration;
        }
    }

    public void Reset(string email) => _entries.TryRemove(Key(email), out _);

    private void Cleanup(DateTime now)
    {
        foreach (var pair in _entries)
        {
            var stale = pair.Value.LockedUntilUtc is { } until ? until <= now : now - pair.Value.WindowStartUtc > _window;
            if (stale)
                _entries.TryRemove(pair.Key, out _);
        }
    }
}
