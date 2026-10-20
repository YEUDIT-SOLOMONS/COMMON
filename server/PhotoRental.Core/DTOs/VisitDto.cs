namespace PhotoRental.Core.DTOs;

// ספירת גולשים ליום: כניסות (פעם אחת לכל ביקור בלשונית) וגולשות חדשות (בפעם הראשונה מהדפדפן)
public class VisitDayDto
{
    public string Day { get; set; } = string.Empty;
    public long Visits { get; set; }
    public long NewVisitors { get; set; }
}
