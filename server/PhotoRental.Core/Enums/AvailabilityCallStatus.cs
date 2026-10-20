namespace PhotoRental.Core.Enums;

// סטטוס שיחת אימות הזמינות (מדומה) אל המשכיר - ראו PhotoRental.Service.Services.SimulatedAvailabilityCallService
public enum AvailabilityCallStatus
{
    Ringing,
    ConfirmedAvailable,
    ConfirmedUnavailable,
    NoAnswer,
    // המשכירה בשעות השקטות שלה - לא חייגנו
    OwnerQuiet
}
