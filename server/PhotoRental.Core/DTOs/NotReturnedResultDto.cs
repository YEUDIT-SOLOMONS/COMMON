namespace PhotoRental.Core.DTOs;

// תוצאת הסימון "הפריט עדיין לא הוחזר" - מה שמוצג למשכירה בחלון שאחרי הלחיצה
public class NotReturnedResultDto
{
    // false = כבר הוחזר או כבר דווח קודם (לא שולחים שוב)
    public bool Reported { get; set; }

    // האם יצאה בפועל שיחת טלפון / נשלח מייל לשוכרת
    public bool CallPlaced { get; set; }

    // כשהשיחה נדחתה כי עכשיו שבת/חג/לילה: מתי היא תצא (שעון ישראל)
    public DateTime? CallScheduledFor { get; set; }
    public bool EmailSent { get; set; }

    // שליח ההחזרה כבר אסף את הפריט מהשוכרת והוא בדרך למשכירה - לא נשלחת תזכורת ולא נספרים ימי איחור
    public bool CourierHasItem { get; set; }

    public int LateDays { get; set; }
    public decimal DailyPrice { get; set; }

    // סכום האיחור עד כה (ימי איחור × מחיר יומי) - שמגיע למשכירה
    public decimal FeeSoFar { get; set; }
}
