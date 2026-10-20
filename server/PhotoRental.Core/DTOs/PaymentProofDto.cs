namespace PhotoRental.Core.DTOs;

public class PaymentProofDto
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string DataUrl { get; set; } = string.Empty;
}
