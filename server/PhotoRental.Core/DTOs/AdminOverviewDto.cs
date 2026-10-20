namespace PhotoRental.Core.DTOs;

// סקירת מנהלת: מספרים כלליים על האתר + נתונים לכל משתמש (למסך הניהול /admin, למנהלות בלבד)
public class AdminOverviewDto
{
    public int TotalUsers { get; set; }
    public int NewUsersLast7Days { get; set; }
    public int ActiveUsersLast7Days { get; set; }
    public int TotalItems { get; set; }
    public int AvailableItems { get; set; }
    public int TotalLoans { get; set; }
    public int ActiveLoans { get; set; }
    public int PaidLoans { get; set; }
    public decimal TotalRevenue { get; set; }
    public List<AdminUserStatsDto> Users { get; set; } = new();
}

public class AdminUserStatsDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? City { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }
    public int ItemsCount { get; set; }
    public int RentalsAsRenter { get; set; }
    public int RentalsOnTheirItems { get; set; }
    public int ActiveRentals { get; set; }
    public decimal TotalSpent { get; set; }
    public decimal TotalEarned { get; set; }
}
