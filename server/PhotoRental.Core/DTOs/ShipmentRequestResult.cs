namespace PhotoRental.Core.DTOs;

// תוצאת הזמנת משלוח אצל חברת השילוח: ערך המעקב להצגה (קישור מעקב או מזהה) + המזהה אצל הספק (לשליפת מצב חי)
public class ShipmentRequestResult
{
    public string TrackingRef { get; set; } = string.Empty;
    public string? ProviderId { get; set; }
}
