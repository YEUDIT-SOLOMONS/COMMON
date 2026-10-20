namespace PhotoRental.Core.DTOs;

// קנייה בלוח יד 2
public class SaleCreateDto
{
    public string ItemId { get; set; } = string.Empty;
    public string DeliveryMethod { get; set; } = "SelfPickup";
    public DeliveryAddressDto? DeliveryAddress { get; set; }
    public string PaymentMethod { get; set; } = "CreditCard";
    public string? PaymentTransactionId { get; set; }
    public string? PaymentProofDataUrl { get; set; }
    public string? PaymentProofFileName { get; set; }
}

public class SaleCreatedDto
{
    public string? Id { get; set; }
    public string Token { get; set; } = string.Empty;
}
