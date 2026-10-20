namespace PhotoRental.Core.Interfaces;

// מצב החוב של משכירה בדוחות העמלה: אחרי שליחת הדוח יש 14 ימים לשלם. ביום ה-7 נשלחת תזכורת, ובסוף 14 הימים - אם עדיין לא
// שולם - היא לא יכולה להוסיף פריטים חדשים, והפריטים שלה מוסתרים מהקטלוג עד לסילוק החוב (והיא מקבלת על כך הודעה).
public interface ICommissionDebtService
{
    // מזהי משכירות שחסומות כרגע (חוב שעבר 14 ימים) - לשימוש הקטלוג ויצירת פריט (נשמר בזיכרון דקות ספורות)
    Task<HashSet<string>> GetBlockedOwnerIdsAsync();

    // מצב החוב של המשכירה לתצוגה באזור האישי: ok / warning / blocked
    Task<OwnerDebtStatus> GetStatusAsync(string ownerId);

    // שולח הודעות: תזכורת (ביום ה-7) והודעת חסימה (ב-14) - פעם אחת לכל דוח. נקרא מסבב שירות הרקע
    Task ProcessNoticesAsync();
}

public class OwnerDebtStatus
{
    // ok / warning (החוב פתוח אך עוד לא נחסם) / blocked
    public string State { get; set; } = "ok";
    public string? Month { get; set; }
    public string? MonthLabel { get; set; }

    // כמה ימים נשארו עד החסימה (במצב warning)
    public int DaysLeft { get; set; }

    // קישור לתשלום באשראי של הדוח הפתוח הוותיק ביותר
    public string? PayUrl { get; set; }
}
