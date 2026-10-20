namespace PhotoRental.Core.Helpers;

public static class NameText
{
    // "רחל כהן" -> "רחל" - לפנייה אישית במיילים
    public static string FirstName(string? name) =>
        (name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
}
