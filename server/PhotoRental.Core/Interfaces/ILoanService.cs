namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;

// מי השתתפה בהשכרה: השוכרת (BorrowerId) והמשכירה (OwnerId, בעלת הפריט) - לבדיקות הרשאה בשכבת ה-API
public record LoanParticipants(string? BorrowerId, string? OwnerId);

public interface ILoanService
{
    // אקספרס ליום עתידי שהגיע זמנו - מזמינים עכשיו את השליח
    Task DispatchPendingExpressAsync();

    // יד 2: מחיר משלוח שליח מהמוכרת לקונה, והזמנת השליח אחרי התשלום
    Task<decimal> GetSaleDeliveryFeeAsync(Item item, DeliveryAddressDto address);
    Task<(string? TrackingId, string? ProviderId, string AddressText)> DispatchSaleAsync(Item item, User buyer, DeliveryAddressDto address);
    Task<(string ProofId, EmailAttachment Attachment)> StoreSaleProofAsync(string? dataUrl, string? fileName);

    Task<IReadOnlyList<LoanResponseDto>> GetAllAsync();
    Task<IReadOnlyList<LoanResponseDto>> GetByBorrowerIdAsync(string borrowerId, int? limit = null);

    // השכרות על הפריטים של משכירה מסוימת ("השכרות על הציוד שלי")
    Task<IReadOnlyList<LoanResponseDto>> GetByOwnerIdAsync(string ownerId, int? limit = null);

    // כמה פעמים כל פריט הושכר - למיון "המבוקשים בקהילה" בקטלוג. מספרים בלבד, בלי פרטי השכרה.
    Task<Dictionary<string, int>> GetRentalCountsAsync();

    Task<LoanParticipants?> GetParticipantsAsync(string loanId);

    // "חלון הסימון" של המשכירה (קישור במייל, בלי התחברות): אימות הטוקן, תצוגה, וסימון נאסף
    Task<bool> ValidateOwnerTokenAsync(string loanId, string? token);
    Task<LoanActionDto?> GetActionViewAsync(string loanId);
    Task MarkPickedUpAsync(string loanId, string? by = null);
    Task<bool> ValidateRenterTokenAsync(string loanId, string? token);
    Task<RenterPickupDto?> GetRenterPickupViewAsync(string loanId);

    // מהחלון "האם הפריט הוחזר?" במייל למשכירה: סימון שהוחזר (לא עושה כלום אם כבר הוחזר)
    Task MarkReturnedFromLinkAsync(string loanId);

    // מהחלון "האם הפריט הוחזר?": סימון שעדיין לא הוחזר - שולח לשוכרת מייל ושיחה (כולל הודעה על קנס של יום נוסף)
    // ומעדכן את תיבת האתר. false אם כבר הוחזר או כבר דווח
    Task<NotReturnedResultDto> ReportNotReturnedAsync(string loanId);
    Task<PaymentProofDto?> GetPaymentProofAsync(string loanId);
    Task<PaymentProofDto?> GetPayoutProofAsync(string loanId);
    Task<OwnerMonthSummaryDto> GetOwnerMonthSummaryAsync(string ownerId);

    // הצעת מחיר משלוח אמיתית (רגיל/אקספרס) עבור פריט וכתובת מסירה נתונים - נקראת מהלקוח עוד לפני
    // אימות הזמינות, כדי שהמחיר המוצג יהיה תמיד המחיר האמיתי שיגבה בפועל (ראו ShippingQuoteDto)
    Task<ShippingQuoteDto> GetShippingQuoteAsync(ShippingQuoteRequestDto request);

    Task<LoanResponseDto> CreateAsync(LoanRequestDto request, CancellationToken ct = default);

    // מצב משלוח חי (זמן הגעה, שליח, מיקום) לשני כיווני ההשכרה
    Task<LoanShippingStatusDto> GetShippingStatusAsync(string loanId);

    // ביטול השכרה שטרם החלה - עד 24 שעות לפני תחילתה, על ידי השוכרת או המשכירה (מנהלת בכל עת)
    Task<CancelLoanResultDto> CancelLoanAsync(string loanId, string actor, bool isAdmin);

    // שינוי אופן ההחזרה באמצע ההשכרה (מסירה עצמית <-> שליח, או כתובת איסוף אחרת)
    Task<LoanResponseDto> ChangeReturnMethodAsync(string loanId, ChangeReturnMethodDto request);

    // מסך "ההזמנה בוצעה": כל ההשכרות של אותה הזמנה (הזמנה מרובת פריטים = כמה השכרות עם אותו OrderGroupId)
    Task<IReadOnlyList<LoanResponseDto>> GetOrderAsync(string loanId);

    // פותח בבת אחת כמה עסקאות השכרה על כמה פריטים מאותו חבר קהילה (משכיר) - "הזמנה" אחת
    // המשתפת מועדים, אמצעי תשלום, ועלות משלוח בודדת (ולא כפולה לכל פריט) - ראו LoanBatchRequestDto.
    Task<IReadOnlyList<LoanResponseDto>> CreateBatchAsync(LoanBatchRequestDto request, CancellationToken ct = default);

    Task ReturnLoanAsync(string id);
    Task UpdateStatusAsync(string id, string status);

    // מסמן תשלום ממתין (מזומן/העברה בנקאית) כשולם בפועל - לשימוש המשכיר, לאחר שקיבל את התשלום
    // בפועל. פותח את חשיפת פרטי הקשר שלו לשוכר (כאשר אופן הקבלה הוא איסוף עצמי - ראו LoanResponseDto).
    Task ConfirmPaymentAsync(string id);
}
