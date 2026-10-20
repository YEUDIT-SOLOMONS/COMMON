namespace PhotoRental.Core.Enums;

// סטטוס הזמינות של פריט ציוד צילום
public enum CopyStatus
{
    // הפריט זמין להשכרה
    Available,

    // הפריט מושכר כרגע
    Rented,

    // הפריט בטיפול/תחזוקה ואינו זמין להשכרה
    Maintenance
}
