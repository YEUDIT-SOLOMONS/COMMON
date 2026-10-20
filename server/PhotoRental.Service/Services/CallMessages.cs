namespace PhotoRental.Service.Services;

using PhotoRental.Core.Helpers;
using System.Globalization;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;

// הנוסחים שהקריין אומר בשיחות הטלפון היוצאות לשוכרת, ועוזרי ניסוח לתאריכים
public static class CallMessages
{
    private static readonly string[] HebrewMonths =
    {
        "ינואר", "פברואר", "מרץ", "אפריל", "מאי", "יוני", "יולי", "אוגוסט", "ספטמבר", "אוקטובר", "נובמבר", "דצמבר"
    };

    // "7 באוקטובר" - כך הקריין אומר תאריך
    public static string? SpokenDate(string? iso)
    {
        if (!DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        return SpokenDate(date);
    }

    public static string SpokenDate(DateTime date) => $"{date.Day} ב{HebrewMonths[date.Month - 1]}";

    // "כשעתיים" / "שעה וחצי" / "כשעה" / "N דקות" - לפי הזמן שנותר בפועל
    public static string DescribeRemaining(TimeSpan remaining)
    {
        var minutes = (int)Math.Round(remaining.TotalMinutes);
        if (minutes >= 105) return "כשעתיים";
        if (minutes >= 75) return "שעה וחצי";
        if (minutes >= 50) return "כשעה";
        return $"{Math.Max(minutes, 1)} דקות";
    }

    // תזכורת החזרה. אם השיחה נדחתה (שבת/חג) ומועד ההחזרה כבר עבר - הנוסח משתנה ל"הייתה אמורה להסתיים".
    // offerExtension: מוסיף הצעה להאריך ביום נוסף בהקשת 1 (רק כשהשיחה אינטראקטיבית)
    public static string Reminder(User borrower, Item item, Loan loan, DateTime nowLocal, bool offerExtension = false)
    {
        var returnTime = loan.ExpectedReturnDate.ToString("HH:mm");
        var remaining = loan.ExpectedReturnDate - nowLocal;

        if (remaining > TimeSpan.Zero)
        {
            return $"שלום {borrower.Name}. זוהי תזכורת אוטומטית מקומון. ההשכרה של {item.Name} מסתיימת בעוד {DescribeRemaining(remaining)}, בשעה {returnTime}. " +
                   "אנא היי זמינה להחזיר את הציוד. שימי לב: איחור בהחזרה מחויב ביום השכרה נוסף על כל יום איחור. " +
                   (offerExtension ? "אם תרצי להאריך את ההשכרה ביום נוסף, הקישי 1. " : string.Empty) +
                   "תודה.";
        }

        var dayDelta = (nowLocal.Date - loan.ExpectedReturnDate.Date).Days;
        var dayLabel = dayDelta switch { 0 => "היום", 1 => "אתמול", _ => SpokenDate(loan.ExpectedReturnDate) };
        return $"שלום {borrower.Name}. זוהי תזכורת אוטומטית מקומון. ההשכרה של {item.Name} הייתה אמורה להסתיים {dayLabel} בשעה {returnTime}. " +
               "אנא החזירי את הציוד בהקדם. שימי לב: איחור בהחזרה מחויב ביום השכרה נוסף על כל יום איחור. תודה.";
    }

    // בקשת הארכה - נאמרת למשכירה (ההקשה: 1 מאשרת, 2 דוחה, 9 שמיעה חוזרת)
    public static string ExtensionRequest(Item item, Loan loan, int days)
    {
        var newReturn = loan.ExpectedReturnDate.AddDays(days);
        var dayWord = days == 1 ? "יום נוסף" : $"{days} ימים נוספים";
        var charged = ShabbatPricing.ChargedExtensionDays(loan.ExpectedReturnDate, days);
        var amount = SpokenAmount(loan.RateOr(item.DailyPrice) * charged * loan.Units);
        var payWay = loan.PaymentMethod == PaymentMethod.CreditCard ? "דרך האתר" : "ישירות";
        var payText = charged == 0 ? "ללא תוספת תשלום, כי זה בשבת או בחג" : $"בתוספת {amount} שישולמו לך {payWay}";
        return $"שלום, זוהי שיחה אוטומטית מקומון. השוכרת של {LoanText.Name(item, loan)} מבקשת להאריך את ההשכרה ב{dayWord}, " +
               $"עד {SpokenDate(newReturn)} בשעה {newReturn:HH:mm}, {payText}. " +
               "אם את מאשרת, לחצי 1. אם לא, לחצי 2. לשמיעה חוזרת, לחצי 9.";
    }

    // עדכון לשוכרת: ההארכה אושרה (loan כבר מעודכן במועד ההחזרה החדש)
    public static string ExtensionApproved(User borrower, Item item, Loan loan) =>
        $"שלום {borrower.Name}. זוהי הודעה אוטומטית מקומון. המשכירה אישרה את ההארכה של {LoanText.Name(item, loan)}. " +
        $"מועד ההחזרה החדש הוא {SpokenDate(loan.ExpectedReturnDate)} בשעה {loan.ExpectedReturnDate:HH:mm}. " +
        (loan.PaymentMethod == PaymentMethod.CreditCard
            ? $"כרטיס האשראי שלך חויב ב{SpokenAmount(loan.RateOr(item.DailyPrice) * Math.Max(1, loan.ExtensionDays) * loan.Units)} על התוספת. תודה."
            : "התשלום על היום הנוסף ישולם ישירות למשכירה. תודה.");

    // עדכון לשוכרת: המשכירה אישרה, אך לא הצלחנו לחייב את הכרטיס השמור - ההארכה לא אושרה
    public static string ExtensionChargeFailed(User borrower, Item item) =>
        $"שלום {borrower.Name}. זוהי הודעה אוטומטית מקומון. המשכירה אישרה את ההארכה של {item.Name}, אך לא הצלחנו לחייב את כרטיס האשראי השמור שלך, ולכן ההארכה לא אושרה. " +
        "אנא החזירי את הציוד בזמן. שימי לב: איחור בהחזרה מחויב ביום השכרה נוסף על כל יום איחור. תודה.";

    // עדכון לשוכרת: ההארכה לא אושרה
    public static string ExtensionDeclined(User borrower, Item item) =>
        $"שלום {borrower.Name}. זוהי הודעה אוטומטית מקומון. המשכירה לא אישרה את ההארכה של {item.Name}. " +
        "אנא החזירי את הציוד בזמן. שימי לב: איחור בהחזרה מחויב ביום השכרה נוסף על כל יום איחור. תודה.";

    // "750 שקלים" / "750 שקלים ו-50 אגורות"
    public static string SpokenAmount(decimal amount)
    {
        var rounded = Math.Round(amount, 2);
        var shekels = (int)Math.Floor(rounded);
        var agorot = (int)Math.Round((rounded - shekels) * 100);
        var shekelWord = shekels == 1 ? "שקל אחד" : $"{shekels} שקלים";
        return agorot > 0 ? $"{shekelWord} ו-{agorot} אגורות" : shekelWord;
    }

    // "האם הפריט הוחזר?" - נאמר למשכירה
    public static string ReturnCheck(User owner, string itemName) =>
        $"שלום {owner.Name}. זוהי שיחה אוטומטית מקומון. האם {itemName} הוחזר אלייך? אם הוחזר, לחצי 1. אם עדיין לא הוחזר, לחצי 2.";

    // "הפריט לא הוחזר" - אחרי שהמשכירה סימנה
    public static string NotReturned(User borrower, string itemName, bool card = false) =>
        $"שלום {borrower.Name}. זוהי הודעה אוטומטית מקומון. המשכירה דיווחה שהציוד {itemName} טרם הוחזר. " +
        "אנא החזירי אותו בהקדם. שימי לב: איחור בהחזרה מחויב ביום השכרה נוסף על כל יום איחור, " + (card ? "ויחויב מכרטיס האשראי שלך. תודה." : "ומשולם ישירות למשכירה. תודה.");

    // שיחת היכרות לחברה חדשה - המספר מוקרא ספרה-ספרה, פעמיים
    public static string Welcome(User user, string spokenNumber) =>
        $"שלום {user.Name}, וברוכה הבאה לקומון! נא שמרי מספר זה באנשי הקשר שלך. זוהי שיחה אוטומטית מהאתר. " +
        "מהמספר הזה נתקשר אלייך רק בעניינים חשובים של ההשכרות שלך. " +
        "כדי שהשיחות שלנו יזוהו תמיד, אנא שמרי את המספר הזה באנשי הקשר בשם קומון. " +
        $"המספר הוא: {spokenNumber}. אני חוזרת: {spokenNumber}. " +
        "תודה, ובהצלחה בהשכרות!";

    // "0 5 5. 4 0 2. 6 8 0 2" - כך הקריין מקריא מספר טלפון ספרה-ספרה
    public static string SpokenPhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("972"))
            digits = "0" + digits[3..];
        if (digits.Length != 10)
            return string.Join(" ", digits.ToCharArray());
        return $"{string.Join(" ", digits[..3].ToCharArray())}. {string.Join(" ", digits[3..6].ToCharArray())}. {string.Join(" ", digits[6..].ToCharArray())}";
    }
}
