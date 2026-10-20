namespace PhotoRental.Core.DTOs;

public record MediaFile(byte[] Bytes, string Mime);

// תצוגה מקדימה לקישור שיתוף (וואטסאפ): הדף להצגה, או כתובת להפניה כשהפריט לא קיים
public record SharePreview(string? Html, string? RedirectUrl);
