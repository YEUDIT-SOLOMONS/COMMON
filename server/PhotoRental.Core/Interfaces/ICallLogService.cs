namespace PhotoRental.Core.Interfaces;

public interface ICallLogService
{
    // רישום מה קרה בשיחה (למשל "לחצה 1"). כשל ביומן לא מפיל את השיחה
    Task SetDetailAsync(string? logId, string detail);

    // עדכון סטטוס מ-Twilio (completed / no-answer / busy...) והפעלת ההמשך המתאים לסוג השיחה
    Task HandleTwilioStatusAsync(string? logId, string callStatus);

    Task<IReadOnlyList<PhotoRental.Core.DTOs.CallLogDto>> GetRecentAsync(int count);

    // שליחה חוזרת של שיחת היכרות: שיחה מתוזמנת חדשה עם אותו נוסח (בשבת/חג - נדחית למוצאי השבת/החג)
    Task<PhotoRental.Core.DTOs.CallResendResultDto> ResendWelcomeAsync(string id);

    // ביטול שיחה שעוד לא יצאה
    Task CancelAsync(string id);
}
