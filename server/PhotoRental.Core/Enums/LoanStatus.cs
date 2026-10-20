namespace PhotoRental.Core.Enums;

// סטטוס עסקת ההשכרה
public enum LoanStatus
{
    // ההשכרה פעילה, הפריט עדיין אצל השוכר
    Active,

    // הפריט הוחזר בהצלחה
    Returned,

    // חלף מועד ההחזרה הצפוי והפריט טרם הוחזר
    Overdue,

    // ההשכרה בוטלה
    Cancelled
}
