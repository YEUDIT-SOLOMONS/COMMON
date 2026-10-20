namespace PhotoRental.Core.Options;

// הגדרות התחברות אמיתיות לחברת השילוח GetPackage - ראו הערה מפורטת ב-GetPackageShippingService.
// כל עוד ApiKey ריק, השרת ממשיך להשתמש בשירות המדומה (SimulatedShippingService) במקום זה.
// לפי התיעוד הרשמי (https://docs.getpackage.com): כתובת הבסיס לסביבת הבדיקות (Sandbox) היא
// https://sandbox-apiv2.getpackage.com - זו גם ברירת המחדל כאן, כדי לא לבצע חיובים/משלוחים
// אמיתיים בטעות. למעבר לסביבת הייצור (אחרי בדיקות מוצלחות ב-Sandbox) יש לפנות לשירות הלקוחות
// של GetPackage לקבלת כתובת ומפתח API לסביבת הייצור, ולעדכן את BaseUrl כאן בהתאם (סביר להניח
// שזו https://apiv2.getpackage.com ללא התחילית "sandbox-", אך יש לאמת זאת מולם ולא להניח).
public class GetPackageOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://sandbox-apiv2.getpackage.com";
}
