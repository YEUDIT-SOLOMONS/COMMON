namespace PhotoRental.API.BackgroundServices;

// External = true כשהמשימות המתוזמנות מופעלות מבחוץ (Cloud Scheduler עם מפתח) - אז הלולאות הפנימיות של השרת כבויות,
// כדי שלא ירוצו פעמיים (כפל קריאות למסד, וסיכון לתזכורת כפולה בשתי אינסטנציות)
public sealed record SchedulerOptions(bool External);
