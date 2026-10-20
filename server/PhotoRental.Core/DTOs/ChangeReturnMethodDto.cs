namespace PhotoRental.Core.DTOs;

// שינוי אופן ההחזרה באמצע ההשכרה ("התחרטתי"): מסירה עצמית <-> איסוף שליח, או החלפת כתובת האיסוף
public class ChangeReturnMethodDto
{
    // "SelfDropoff" או "CourierPickup"
    public string ReturnMethod { get; set; } = "SelfDropoff";

    // כתובת איסוף מלאה - חובה כש-ReturnMethod="CourierPickup"
    public DeliveryAddressDto? ReturnPickupAddress { get; set; }
}
