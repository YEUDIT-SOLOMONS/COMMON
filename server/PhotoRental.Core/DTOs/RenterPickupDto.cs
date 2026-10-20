namespace PhotoRental.Core.DTOs;

// חלון "קיבלת את הפריט?" של השוכרת (קישור במייל, בלי התחברות)
public class RenterPickupDto
{
    public string LoanId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string DeliveryMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? PickedUpAt { get; set; }
    public string? PickedUpBy { get; set; }
}
