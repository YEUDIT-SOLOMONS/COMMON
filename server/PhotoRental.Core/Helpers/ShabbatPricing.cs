namespace PhotoRental.Core.Helpers;

// יום השכרה שמתחיל בשבת או בחג (שאינו היום הראשון) לא מחויב
public static class ShabbatPricing
{
    public static int ChargedDays(DateTime startDate, int spanDays)
    {
        if (spanDays <= 1)
            return Math.Max(1, spanDays);
        var free = Enumerable.Range(1, spanDays - 1).Count(i => CallWindow.IsHolyDay(startDate.Date.AddDays(i)));
        return Math.Max(1, spanDays - free);
    }

    // הארכה: הימים מתחילים במועד ההחזרה הקודם, ושבת/חג ביניהם לא מחויבים
    public static int ChargedExtensionDays(DateTime previousReturn, int days) =>
        Math.Max(0, days - Enumerable.Range(0, Math.Max(0, days)).Count(j => CallWindow.IsHolyDay(previousReturn.Date.AddDays(j))));
}
