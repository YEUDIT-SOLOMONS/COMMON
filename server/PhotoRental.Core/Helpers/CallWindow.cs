namespace PhotoRental.Core.Helpers;

using System.Globalization;

// חלון שיחות הטלפון האוטומטיות: כל שעות היממה, חוץ משבת וחגי ישראל (ראש השנה, יום כיפור, סוכות, שמיני עצרת/שמחת תורה,
// פסח (ראשון ואחרון), שבועות). שבת/חג נחשבים מ-40 דקות לפני השקיעה בערב ועד 72 דקות אחרי השקיעה ביום עצמו
// (זמנים שמרניים - עדיף להמתין עוד קצת). שיחה שנופלת בזמן אסור נדחית לרגע המותר הראשון שאחריו, כלומר למוצאי
// השבת/החג. החישוב לפי שעון ישראל (בני ברק) והלוח העברי של .NET.
public static class CallWindow
{
    private const double Latitude = 32.0840;   // בני ברק
    private const double Longitude = 34.8338;
    private static readonly TimeSpan BeforeSunset = TimeSpan.FromMinutes(40);
    private static readonly TimeSpan AfterSunset = TimeSpan.FromMinutes(72);

    private static readonly HebrewCalendar Hebrew = new();

    // האם מותר להתקשר עכשיו (שעון ישראל)
    public static bool IsAllowed(DateTime israelLocal) => !IsShabbatOrYomTov(israelLocal);

    // הרגע המותר הראשון (שעון ישראל) החל מ-israelLocal - מוצאי שבת/חג אם הזמן נופל עליהם
    public static DateTime NextAllowed(DateTime israelLocal)
    {
        var t = DateTime.SpecifyKind(israelLocal, DateTimeKind.Unspecified);
        var guard = 0;
        while (IsShabbatOrYomTov(t) && guard++ < 24 * 12 * 4)
            t = t.AddMinutes(5);
        return t;
    }

    public static bool IsShabbatOrYomTov(DateTime israelLocal)
    {
        foreach (var day in new[] { israelLocal.Date, israelLocal.Date.AddDays(1) })
        {
            if (!IsHolyDay(day)) continue;
            var start = SunsetLocal(day.AddDays(-1)) - BeforeSunset;
            var end = SunsetLocal(day) + AfterSunset;
            if (israelLocal >= start && israelLocal < end) return true;
        }
        return false;
    }

    // שבת או יום טוב (בישראל - יום אחד בכל חג)
    public static bool IsHolyDay(DateTime day)
    {
        if (day.DayOfWeek == DayOfWeek.Saturday) return true;

        var year = Hebrew.GetYear(day);
        var month = Hebrew.GetMonth(day);
        var d = Hebrew.GetDayOfMonth(day);
        var leap = Hebrew.IsLeapYear(year);
        var nisan = leap ? 8 : 7;
        var sivan = leap ? 10 : 9;

        return (month == 1 && (d == 1 || d == 2 || d == 10 || d == 15 || d == 22))
               || (month == nisan && (d == 15 || d == 21))
               || (month == sivan && d == 6);
    }

    // שקיעה בשעון ישראל לתאריך נתון (אלגוריתם Almanac הסטנדרטי, דיוק של כדקה-שתיים)
    public static DateTime SunsetLocal(DateTime day)
    {
        const double zenith = 90.8333;
        static double Rad(double deg) => deg * Math.PI / 180.0;
        static double Deg(double rad) => rad * 180.0 / Math.PI;
        static double Norm(double v, double max) { v %= max; return v < 0 ? v + max : v; }

        var n = day.DayOfYear;
        var lngHour = Longitude / 15.0;
        var t = n + ((18 - lngHour) / 24.0);
        var m = (0.9856 * t) - 3.289;
        var l = Norm(m + (1.916 * Math.Sin(Rad(m))) + (0.020 * Math.Sin(Rad(2 * m))) + 282.634, 360);
        var ra = Norm(Deg(Math.Atan(0.91764 * Math.Tan(Rad(l)))), 360);
        ra += (Math.Floor(l / 90) * 90) - (Math.Floor(ra / 90) * 90);
        ra /= 15.0;
        var sinDec = 0.39782 * Math.Sin(Rad(l));
        var cosDec = Math.Cos(Math.Asin(sinDec));
        var cosH = (Math.Cos(Rad(zenith)) - (sinDec * Math.Sin(Rad(Latitude)))) / (cosDec * Math.Cos(Rad(Latitude)));
        var h = Deg(Math.Acos(Math.Clamp(cosH, -1, 1))) / 15.0;
        var localMean = h + ra - (0.06571 * t) - 6.622;
        var utHours = Norm(localMean - lngHour, 24);

        var utc = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc).AddHours(utHours);
        return IsraelTime.FromUtc(utc);
    }

    // "ביום ראשון בשעה 20:10" - לשימוש בהודעות
    public static string Describe(DateTime israelLocal)
    {
        var he = CultureInfo.GetCultureInfo("he-IL");
        return $"ביום {israelLocal.ToString("dddd", he).Replace("יום ", string.Empty)} בשעה {israelLocal:HH:mm}";
    }
}
