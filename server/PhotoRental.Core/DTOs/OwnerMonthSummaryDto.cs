namespace PhotoRental.Core.DTOs;

// סיכום החודש הנוכחי למשכירה: כמה פעמים הציוד שלה הושכר (הושכר והוחזר) וכמה הכניס לה אחרי העמלה
public class OwnerMonthSummaryDto
{
    public int Rentals { get; set; }
    public decimal Earned { get; set; }
}
