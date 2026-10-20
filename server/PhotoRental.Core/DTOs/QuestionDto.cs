namespace PhotoRental.Core.DTOs;

// "יש לי שאלה למשכירה": התכתבות בין שוכרת למשכירה על פריט
public class QuestionCreatedDto
{
    public string? Id { get; set; }
    public string Token { get; set; } = string.Empty;
}

public class QuestionSummaryDto
{
    public string? Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string? Image { get; set; }
    public DateTime LastMessageAtUtc { get; set; }
    public int Count { get; set; }
    public bool AwaitingMe { get; set; }
    public bool Unread { get; set; }
    public string? LastText { get; set; }
}

public class QuestionItemDto
{
    public string? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Image { get; set; }
    public string? City { get; set; }
}

public class QuestionMessageDto
{
    public string From { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime AtUtc { get; set; }
    public List<string> ImageIds { get; set; } = new();
}

public class QuestionThreadDto
{
    public string? Id { get; set; }
    public string Role { get; set; } = string.Empty;
    public QuestionItemDto? Item { get; set; }
    public string ViewToken { get; set; } = string.Empty;
    public List<QuestionMessageDto> Messages { get; set; } = new();
}

public record QuestionImageFile(byte[] Bytes, string Mime);
