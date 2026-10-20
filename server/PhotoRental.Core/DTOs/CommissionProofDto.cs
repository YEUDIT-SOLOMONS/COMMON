namespace PhotoRental.Core.DTOs;

// מה שמוצג בחלון "העלאת אסמכתא" שנפתח מהקישור במייל הדוח החודשי (בלי התחברות)
public class CommissionProofViewDto
{
    public string OwnerName { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public string MonthLabel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool AlreadyUploaded { get; set; }
    public bool Paid { get; set; }
}

public class CommissionProofUploadDto
{
    public string? DataUrl { get; set; }
    public string? FileName { get; set; }
}
