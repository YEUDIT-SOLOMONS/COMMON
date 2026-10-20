namespace PhotoRental.Core.DTOs;

// נשלח מהלקוח דרך טופס "יצירת קשר"
public class ContactMessageCreateDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
