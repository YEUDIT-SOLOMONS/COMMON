namespace PhotoRental.Core.Helpers;

using System.Text.RegularExpressions;

// קישורי תמונות הפריטים: התמונות עצמן לא נשלחות בתוך נתוני הקטלוג, אלא כקישור /img/... באותה כתובת של האתר
// (נשמר בקאש של הדפדפן ושל Firebase). v = טביעה של תוכן התמונה, כך שתמונה שהוחלפה מקבלת קישור חדש
public static class ImageLinks
{
    private static readonly Regex Own = new(@"^/img/(?<id>[^/?]+)/(?<slot>\d+|thumb)", RegexOptions.Compiled);

    public static string Version(string dataUrl)
    {
        uint hash = 2166136261;
        foreach (var c in dataUrl)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return $"{hash:x8}{dataUrl.Length:x}";
    }

    public static string? Thumb(string? itemId, string? dataUrl) =>
        string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(dataUrl) ? null
        : !IsData(dataUrl) ? dataUrl
        : $"/img/{itemId}/thumb?v={Version(dataUrl)}";

    // תמונה שנשמרה ככתובת חיצונית (ולא כקובץ) - מוחזרת כמו שהיא
    public static string Full(string itemId, int index, string dataUrl) =>
        !IsData(dataUrl) ? dataUrl : $"/img/{itemId}/{index}?v={Version(dataUrl)}";

    private static bool IsData(string value) => value.StartsWith("data:", StringComparison.Ordinal);

    // בעריכת פריט הלקוח שולח בחזרה קישורים לתמונות הקיימות - מתרגמים אותם חזרה לתמונה השמורה
    public static int? ExistingIndex(string value, string itemId)
    {
        var m = Own.Match(value);
        if (!m.Success || m.Groups["id"].Value != itemId) return null;
        return m.Groups["slot"].Value == "thumb" ? 0 : int.TryParse(m.Groups["slot"].Value, out var i) ? i : null;
    }
}
