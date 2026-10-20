namespace PhotoRental.Core.Options;

// מזהה פרויקט ה-Firebase - זהה לזה המוגדר תחת "Firebase:ProjectId" ב-appsettings.json (המשמש
// גם לחיבור Firestore הרגיל). נדרש כדי לאמת אסימוני זהות (ID tokens) שמתקבלים מ-Firebase
// Authentication בצד הלקוח (למשל לאחר התחברות עם Google דרך הפופ-אפ של Firebase) - ראו הערה
// מפורטת ב-AuthService.LoginWithGoogleAsync. אין כאן שום מפתח סודי - רק מזהה הפרויקט הציבורי.
public class FirebaseAuthOptions
{
    public string ProjectId { get; set; } = string.Empty;
}
