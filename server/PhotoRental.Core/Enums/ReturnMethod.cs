namespace PhotoRental.Core.Enums;

// אופן החזרת הפריט בתום ההשכרה - נקבע מראש כבר בעת פתיחת ההשכרה, יחד עם מועד ההחזרה המשוער
public enum ReturnMethod
{
    // השוכר מחזיר את הפריט בעצמו ישירות למשכיר
    SelfDropoff,

    // שליח אוסף את הפריט מהשוכר ומחזיר אותו למשכיר
    CourierPickup
}
