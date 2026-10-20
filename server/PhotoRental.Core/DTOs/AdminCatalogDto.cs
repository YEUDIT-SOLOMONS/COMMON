namespace PhotoRental.Core.DTOs;

// מסך הניהול: רחובות ושכונות, פריטים, התכתבויות ושליחה חוזרת של שיחות
public class AdminStreetDto
{
    public string City { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public int Items { get; set; }
    public string? Neighborhood { get; set; }
}

public class AdminNeighborhoodsDto
{
    public List<AdminStreetDto> Rows { get; set; } = new();
    public Dictionary<string, string[]> Options { get; set; } = new();
}

public class AdminItemDto
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string? CategoryId { get; set; }
    public string? ExtraCategoryId { get; set; }
    public string? Image { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
}

public class AdminQuestionDto
{
    public string? Id { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string RenterName { get; set; } = string.Empty;
    public string RenterEmail { get; set; } = string.Empty;
    public DateTime LastMessageAtUtc { get; set; }
    public string OwnerToken { get; set; } = string.Empty;
    public List<QuestionMessageDto> Messages { get; set; } = new();
}

public class CallResendResultDto
{
    public string? Id { get; set; }
    public DateTime? ScheduledForLocal { get; set; }
}
