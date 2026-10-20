namespace PhotoRental.Data;

using System.Globalization;
using System.Text.Json.Nodes;

// מחלקת עזר לבניית וניתוח ערכי שדות בפורמט ה-JSON הטיפוסי (Typed Value) של Firestore REST API,
// שבו כל ערך עטוף באובייקט המציין את סוגו (למשל {"stringValue": "..."} או {"doubleValue": 1.5}).
// משמשת את שכבת ה-Repositories כדי להמיר בין ישויות ה-C# (Core.Entities) לבין המבנה הזה, וחזרה.
public static class FirestoreJson
{
    // ===================== בניית ערכי שדות (לשליחה ל-Firestore) =====================

    public static JsonObject StringField(string value) => new() { ["stringValue"] = value };

    public static JsonObject StringOrNullField(string? value) =>
        value is null ? NullField() : StringField(value);

    public static JsonObject DoubleField(double value) => new() { ["doubleValue"] = value };

    public static JsonObject DoubleOrNullField(double? value) =>
        value.HasValue ? DoubleField(value.Value) : NullField();

    public static JsonObject BooleanField(bool value) => new() { ["booleanValue"] = value };

    // תאריך/שעה נשמרים כמחרוזת RFC 3339 ב-UTC, כפי שדורש Firestore REST API עבור timestampValue
    public static JsonObject TimestampField(DateTime value) =>
        new() { ["timestampValue"] = value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) };

    public static JsonObject TimestampOrNullField(DateTime? value) =>
        value.HasValue ? TimestampField(value.Value) : NullField();

    public static JsonObject NullField()
    {
        var obj = new JsonObject();
        obj["nullValue"] = null;
        return obj;
    }

    // ===================== קריאת ערכי שדות (מתוך תשובת Firestore) =====================

    public static string GetString(JsonObject? fields, string name, string defaultValue = "")
    {
        return fields?[name]?["stringValue"]?.GetValue<string>() ?? defaultValue;
    }

    public static string? GetStringOrNull(JsonObject? fields, string name)
    {
        return fields?[name]?["stringValue"]?.GetValue<string>();
    }

    public static double GetDouble(JsonObject? fields, string name)
    {
        return fields?[name]?["doubleValue"]?.GetValue<double>() ?? 0;
    }

    public static double? GetDoubleOrNull(JsonObject? fields, string name)
    {
        return fields?[name]?["doubleValue"]?.GetValue<double>();
    }

    public static bool GetBool(JsonObject? fields, string name, bool defaultValue = false)
    {
        return fields?[name]?["booleanValue"]?.GetValue<bool>() ?? defaultValue;
    }

    public static DateTime GetTimestamp(JsonObject? fields, string name)
    {
        var raw = fields?[name]?["timestampValue"]?.GetValue<string>();
        return raw is null
            ? default
            : DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public static DateTime? GetTimestampOrNull(JsonObject? fields, string name)
    {
        var raw = fields?[name]?["timestampValue"]?.GetValue<string>();
        return raw is null
            ? null
            : DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    public static TEnum GetEnum<TEnum>(JsonObject? fields, string name, TEnum defaultValue) where TEnum : struct, Enum
    {
        var raw = fields?[name]?["stringValue"]?.GetValue<string>();
        return raw is not null && Enum.TryParse<TEnum>(raw, out var parsed) ? parsed : defaultValue;
    }

    // חילוץ מזהה המסמך (המקטע האחרון בנתיב) מתוך שדה ה-"name" המלא שמוחזר ע"י Firestore,
    // לדוגמה: "projects/rent-photo/databases/(default)/documents/Items/abc123" -> "abc123"
    public static string? ExtractDocumentId(JsonObject? document)
    {
        var fullName = document?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(fullName))
        {
            return null;
        }

        var lastSlash = fullName.LastIndexOf('/');
        return lastSlash >= 0 ? fullName[(lastSlash + 1)..] : fullName;
    }
}
