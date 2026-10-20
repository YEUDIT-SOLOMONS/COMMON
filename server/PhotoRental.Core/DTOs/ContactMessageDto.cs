namespace PhotoRental.Core.DTOs;

using System.ComponentModel.DataAnnotations;

// נשלח מהלקוח דרך טופס "יצירת קשר"
public class ContactMessageCreateDto
{
    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    [StringLength(4000)]
    public string Message { get; set; } = string.Empty;
}
