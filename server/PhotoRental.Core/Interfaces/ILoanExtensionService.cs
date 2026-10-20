namespace PhotoRental.Core.Interfaces;

// תיאום הארכת השכרה: השוכרת מבקשת יום נוסף בשיחת התזכורת (הקשת 1), המשכירה מאשרת או דוחה בשיחת טלפון (1/2),
// ושתיהן מקבלות עדכון. בקשה שלא אושרה עד מועד ההחזרה נחשבת כדחויה.
public interface ILoanExtensionService
{
    // השוכרת הקישה 1 בשיחת התזכורת. מחזיר את ההודעה שנאמרת לה בטלפון
    Task<string> RequestFromRenterAsync(string loanId);

    // המשכירה הקישה ספרה בשיחת האישור. מחזיר את ההודעה שנאמרת לה בטלפון
    Task<string> OwnerAnsweredAsync(string loanId, string? digits);

    // נוסח בקשת ההארכה למשכירה (לשמיעה חוזרת בהקשת 9); null אם הבקשה כבר לא ממתינה
    Task<string?> GetOwnerMessageAsync(string loanId);

    // שיחת האישור הסתיימה בלי תשובה - ניסיון חוזר אחד בעוד כמה דקות
    Task OwnerCallEndedWithoutAnswerAsync(string loanId);

    // בקשות שלא אושרו עד מועד ההחזרה: נסגרות כדחויות והשוכרת מקבלת הודעה (נקרא בכל סבב של שירות הרקע)
    Task ExpirePendingAsync();
}
