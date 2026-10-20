namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IReviewService
{
    Task<ItemReviewsDto> GetItemReviewsAsync(string itemId);
    Task<Dictionary<string, RatingSummaryDto>> GetSummaryAsync();
    Task<IReadOnlyList<PendingReviewDto>> GetPendingAsync(string userId);
    Task SubmitAsync(string userId, string loanId, ReviewSubmitDto dto);

    // "מילים טובות שקיבלת" - לאזור האישי של המשכירה
    Task<IReadOnlyList<ThankYouNoteDto>> GetThankYouNotesAsync(string ownerId);

    // "הסרה בלי לדרג" - ההשכרה יורדת מרשימת הדירוגים של המשתמשת
    Task DismissAsync(string userId, string loanId);

    Task<IReadOnlyList<AdminReviewDto>> GetAllForAdminAsync();
    Task SetHiddenAsync(string reviewId, bool hidden);

    // לשירות הרקע: יום אחרי ההחזרה - מייל לשוכרת ולמשכירה עם בקשה לדרג
    Task SendReviewRequestsAsync();
}

// תזכורות לפני קבלת הציוד: מייל בערב שלפני, שיחה לשוכרת שעה לפני איסוף עצמי ושיחה למשכירה שעתיים לפני
public interface IPickupReminderService
{
    Task ProcessAsync();
}
