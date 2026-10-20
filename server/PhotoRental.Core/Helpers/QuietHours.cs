namespace PhotoRental.Core.Helpers;

using System.Globalization;
using PhotoRental.Core.Entities;

// "אין לחייג אלי בלילה": שעות שקטות שכל חברה בוחרת לעצמה (ברירת מחדל 00:30-07:00), בנוסף לשבת וחג
public static class QuietHours
{
    public const string DefaultFrom = "00:30";
    public const string DefaultTo = "07:00";

    public static bool IsQuiet(User? user, DateTime israelLocal)
    {
        if (user is null || !user.QuietHoursEnabled) return false;
        var from = Parse(user.QuietFrom, DefaultFrom);
        var to = Parse(user.QuietTo, DefaultTo);
        if (from == to) return false;
        var t = israelLocal.TimeOfDay;
        return from < to ? t >= from && t < to : t >= from || t < to;
    }

    public static DateTime NextAllowed(User? user, DateTime israelLocal)
    {
        var t = CallWindow.NextAllowed(israelLocal);
        for (var i = 0; i < 6 && IsQuiet(user, t); i++)
        {
            var to = Parse(user!.QuietTo, DefaultTo);
            var end = t.Date + to;
            if (end <= t) end = end.AddDays(1);
            t = CallWindow.NextAllowed(end);
        }
        return t;
    }

    public static bool IsAllowed(User? user, DateTime israelLocal) =>
        CallWindow.IsAllowed(israelLocal) && !IsQuiet(user, israelLocal);

    public static string? Normalize(string? value) =>
        TimeSpan.TryParseExact(value?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1)
            ? t.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : null;

    private static TimeSpan Parse(string? value, string fallback) =>
        TimeSpan.ParseExact(Normalize(value) ?? fallback, @"hh\:mm", CultureInfo.InvariantCulture);
}
