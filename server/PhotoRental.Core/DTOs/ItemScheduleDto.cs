namespace PhotoRental.Core.DTOs;

// לוח הזמנים של פריט. Busy - טווחים תפוסים (לכולן, בלי סיבה). Rentals/Blocks - רק למשכירה עצמה
public class ItemScheduleDto
{
    public bool IsOwner { get; set; }

    // כמה עותקים יש לפריט, וכמה יחידות תפוסות בכל השכרה - כדי לחשב כמה יחידות פנויות בטווח שנבחר
    public int Quantity { get; set; } = 1;
    public List<BookingDto> Bookings { get; set; } = new();
    public List<BusyRangeDto> Blocked { get; set; } = new();
    public List<BusyRangeDto> Busy { get; set; } = new();
    public List<ScheduleRentalDto> Rentals { get; set; } = new();
    public List<ItemBlockDto> Blocks { get; set; } = new();
}

// שעון קיר של ישראל
public class BusyRangeDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class BookingDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int Units { get; set; } = 1;
}

public class ScheduleRentalDto
{
    public string LoanId { get; set; } = string.Empty;
    public int Units { get; set; } = 1;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}

public class ItemBlockDto
{
    public string Id { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Note { get; set; }
}

public class ItemBlockCreateDto
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string? Note { get; set; }
}
