namespace PhotoRental.Service.Services;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Interfaces;

// מדמה הצעת מחיר ובקשת איסוף/משלוח אוטומטית לחברת שילוח: מחזיר מחיר קבוע ו"שולח" בקשה (בזיכרון
// בלבד, ללא קריאת רשת אמיתית) עם מזהה מעקב מדומה - כדי שהתכונה תישאר שמישה להדגמה עד לחיבור
// חברת שילוח אמיתית (ראו GetPackageShippingService), באותו העיקרון בדיוק שכבר קיים
// ב-SimulatedAvailabilityCallService וב-SimulatedUpayPaymentService.
public class SimulatedShippingService : IShippingService
{
    // מחירי ברירת מחדל למשלוח מדומה - זהים לאלו ששימשו את LoanService לפני חיבור הצעת מחיר אמיתית
    private const decimal RegularPrice = 25m;
    private const decimal ExpressPrice = 45m;

    public Task<ShippingQuoteDto> GetQuoteAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress)
    {
        return Task.FromResult(new ShippingQuoteDto { RegularPrice = RegularPrice, ExpressPrice = ExpressPrice });
    }

    public Task<ShipmentRequestResult> RequestPickupAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress, DateTime scheduledDate, bool isExpress, TimeSpan? mustArriveBy = null)
    {
        var trackingId = $"SIM-SHIP-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        return Task.FromResult(new ShipmentRequestResult { TrackingRef = trackingId });
    }

    public Task<ArrivalCheckDto> CheckArrivalAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress, DateTime date, TimeSpan mustArriveBy, bool isExpress) =>
        Task.FromResult(new ArrivalCheckDto { Possible = true, Verified = false });

    // במצב מדומה אין מעקב חי
    public Task<ShipmentStatusDto?> GetStatusAsync(string providerId) => Task.FromResult<ShipmentStatusDto?>(null);

    public Task CancelAsync(string providerId) => Task.CompletedTask;
}
