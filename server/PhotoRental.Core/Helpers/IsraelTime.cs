namespace PhotoRental.Core.Helpers;

// שעון ישראל. מועדי ההשכרה (תחילה/החזרה) נשמרים כשעון קיר מקומי של ישראל (בלי המרה), ולכן כל השוואה
// ל"עכשיו" חייבת להיעשות מול שעון ישראל ולא מול UTC. חישוב איחור: כל יום (או חלק מיום) מעבר למועד
// ההחזרה נספר כיום השכרה נוסף.
public static class IsraelTime
{
    private static TimeZoneInfo? FindZone()
    {
        foreach (var id in new[] { "Israel Standard Time", "Asia/Jerusalem" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return null;
    }

    private static readonly TimeZoneInfo? Zone = FindZone();

    public static DateTime FromUtc(DateTime utc)
    {
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = Zone is null ? asUtc + FallbackOffset(asUtc) : TimeZoneInfo.ConvertTimeFromUtc(asUtc, Zone);
        // שעון קיר מקומי - בלי סימון UTC, כדי שהדפדפן לא יזיז אותו שוב
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    // כשבמכונה אין מסד אזורי הזמן (tzdata): חוקי שעון הקיץ בישראל - מתחיל ביום שישי שלפני יום ראשון האחרון של מרץ
    // ב-02:00 (00:00 UTC), ומסתיים ביום ראשון האחרון של אוקטובר ב-02:00 שעון קיץ (23:00 UTC של הערב שלפני).
    // היסט: +3 בקיץ, +2 בחורף.
    private static TimeSpan FallbackOffset(DateTime utc)
    {
        static DateTime LastSunday(int year, int month)
        {
            var last = new DateTime(year, month, DateTime.DaysInMonth(year, month), 0, 0, 0, DateTimeKind.Utc);
            return last.AddDays(-(int)last.DayOfWeek);
        }

        var dstStart = LastSunday(utc.Year, 3).AddDays(-2);
        var dstEnd = LastSunday(utc.Year, 10).AddDays(-1).AddHours(23);
        return utc >= dstStart && utc < dstEnd ? TimeSpan.FromHours(3) : TimeSpan.FromHours(2);
    }

    public static DateTime Now => FromUtc(DateTime.UtcNow);

    // תקופת חסד אחרי מועד ההחזרה: לפעמים לא מסמנים מיד, ולכן יום נוסף נספר רק אחרי 90 דקות
    // (בהשכרה ליום אחד - 25.5 שעות מתחילת ההשכרה)
    public static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(90);

    // כמה ימי איחור: 0 עד שחלפה תקופת החסד, אחרת ימים מלאים + חלקי יום (מעוגל כלפי מעלה)
    public static int LateDays(DateTime expectedReturnLocal, DateTime nowLocal)
    {
        var late = nowLocal - expectedReturnLocal - LateGrace;
        return late <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(late.TotalDays);
    }
}
