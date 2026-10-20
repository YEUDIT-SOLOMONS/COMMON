namespace PhotoRental.Service.Services;

// בדיקת קובץ אסמכתא שהועלה כ-Data URL (תמונה, PDF או אקסל): סוג, תקינות base64, גודל ושם קובץ בטוח
public static class ProofFileValidator
{
    private const int MaxChars = 900_000;

    private static readonly Dictionary<string, string> ExtensionByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg", ["image/png"] = ".png", ["image/webp"] = ".webp", ["image/gif"] = ".gif", ["application/pdf"] = ".pdf",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx", ["application/vnd.ms-excel"] = ".xls", ["text/csv"] = ".csv"
    };

    public record ValidatedProof(string FileName, string ContentType, string Base64);

    public static ValidatedProof Validate(string? dataUrl, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(dataUrl))
            throw new ArgumentException("יש להעלות קובץ אסמכתא");
        if (dataUrl.Length > MaxChars)
            throw new ArgumentException("קובץ האסמכתא גדול מדי - נא להעלות קובץ קטן יותר");

        var comma = dataUrl.IndexOf(',');
        var header = comma > 0 ? dataUrl[..comma] : string.Empty;
        var contentType = header.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)
            ? header["data:".Length..^";base64".Length]
            : string.Empty;
        if (!ExtensionByType.TryGetValue(contentType, out var extension))
            throw new ArgumentException("קובץ האסמכתא חייב להיות תמונה, PDF או קובץ אקסל");

        var base64 = dataUrl[(comma + 1)..];
        if (!Convert.TryFromBase64String(base64, new byte[base64.Length], out _))
            throw new ArgumentException("קובץ האסמכתא פגום - נא להעלות אותו שוב");

        var safeName = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "asmachta" + extension;
        else if (!Path.HasExtension(safeName))
            safeName += extension;
        if (safeName.Length > 80)
            safeName = safeName[^80..];

        return new ValidatedProof(safeName, contentType, base64);
    }
}
