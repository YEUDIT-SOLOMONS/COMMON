namespace PhotoRental.Core.Exceptions;

// כשל בגישה לבסיס הנתונים (רשת, הרשאות, תשובת שגיאה) - תקלה אמיתית, לא החלטה עסקית.
// יורשת מ-InvalidOperationException כדי שקוד קיים שתופס אותה ימשיך לעבוד
public class DataAccessException : InvalidOperationException
{
    public DataAccessException(string message, Exception? inner = null) : base(message, inner) { }
}
