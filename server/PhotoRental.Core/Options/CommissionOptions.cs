namespace PhotoRental.Core.Options;

// עמלת השירות של האתר (נקבעת ב-appsettings / משתני סביבה, מקטע "Commission"):
//  - Percent: אחוז העמלה מסכום ההשכרה (ברירת מחדל 8)
//  - FirstBillingMonth: החודש הראשון שבו נשלחים דוחות חודשיים אוטומטית ("yyyy-MM") - חודשים קודמים לא נשלחים
//  - PaymentLinkUrl: קישור לתשלום באשראי (דף תשלום של UPAY וכד') - מופיע בדוח כלחצן "לתשלום באשראי"
//  - BankDetails: פרטי חשבון הבנק של האתר להעברה בנקאית (שורות מופרדות ב-| או בירידת שורה)
//  - PaymentInstructions: טקסט חופשי נוסף שמופיע בדוח החודשי לגבי אופן התשלום
public class CommissionOptions
{
    public decimal Percent { get; set; } = 8m;
    public string FirstBillingMonth { get; set; } = "2026-10";
    public string PaymentInstructions { get; set; } = string.Empty;
    public string PaymentLinkUrl { get; set; } = string.Empty;
    public string BankDetails { get; set; } = string.Empty;
}
