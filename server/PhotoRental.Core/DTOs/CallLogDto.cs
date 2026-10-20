namespace PhotoRental.Core.DTOs;

// שורה ביומן השיחות במסך הניהול
public class CallLogDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;
    public string ToPhone { get; set; } = string.Empty;
    public string? InterestedEmail { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string Status { get; set; } = string.Empty;

    // שעון ישראל (שעון קיר, ללא אזור זמן)
    public DateTime? ScheduledForLocal { get; set; }
    public string? Detail { get; set; }
    public string? Message { get; set; }
}
