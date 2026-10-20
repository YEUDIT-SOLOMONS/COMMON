namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;

public interface ILoanRepository
{
    Task<IReadOnlyList<Loan>> GetAllAsync();
    Task<Loan?> GetByIdAsync(string id);
    Task DeleteAsync(string id);
    Task<IReadOnlyList<Loan>> GetActiveLoansForItemAsync(string itemId);
    Task<IReadOnlyList<Loan>> GetByBorrowerIdAsync(string borrowerId);
    Task<Loan> CreateAsync(Loan loan);
    Task UpdateStatusAsync(string id, LoanStatus status, DateTime? actualReturnDate = null, string? returnedBy = null);
    Task UpdatePaymentStatusAsync(string id, PaymentStatus status);
    Task UpdateReturnReminderSentAsync(string id, DateTime sentAtUtc);
    Task UpdateLateDaysAsync(string id, int lateDays);
    Task UpdatePickedUpAsync(string id, DateTime pickedUpAtUtc, string? by = null);
    Task UpdatePickupPromptSentAsync(string id, DateTime sentAtUtc);
    Task UpdateOutboundDispatchAsync(string id, string? trackingId, string? providerId, DateTime? expressDispatchAtLocal);
    Task UpdateReturnPromptSentAsync(string id, DateTime sentAtUtc);
    Task UpdateNotReturnedReportedAsync(string id, DateTime reportedAtUtc);

    // מצב בקשת ההארכה (status = null מנקה)
    Task UpdateExtensionAsync(string id, string? status, int days, DateTime? requestedAtUtc, int attempts);

    Task UpdateCancellationAsync(string id, DateTime cancelledAtUtc, string cancelledBy);
    Task UpdateOwnerPayoutAsync(string id, DateTime paidAtUtc, decimal amount, string? proofId = null);
    Task UpdateShippingChargeAsync(string id, string? status, string? paymentId, decimal amount);

    // תוכנית ההחזרה (אופן, מעקב, מזהה אצל הספק וכתובת איסוף) - נקבעת מחדש כשהשוכרת משנה את אופן ההחזרה
    Task UpdateReturnPlanAsync(string id, ReturnMethod method, string? trackingId, string? providerId, string? pickupAddress, decimal returnFee);

    // הארכה שאושרה: מועד החזרה חדש, ימי השכרה מעודכנים, ותזכורת החזרה חדשה (מאפסים את הישנה)
    Task ApplyExtensionAsync(string id, DateTime newExpectedReturnDate, int newRentalDays, decimal extensionChargedTotal);

    Task UpdateLateFeeChargedAsync(string id, decimal amount);

    // שאילתות במקום סריקת כל ההשכרות - בשימוש הסבב המתוזמן
    Task<IReadOnlyList<Loan>> GetByStatusAsync(LoanStatus status);

    Task<IReadOnlyList<Loan>> GetByExtensionStatusAsync(string extensionStatus);
}
