namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface ICommissionService
{
    Task<CommissionReportDto> GetOwnerReportAsync(string ownerId, string? month);
    Task<CommissionAdminOverviewDto> GetAdminOverviewAsync(string? month);
    Task SetPaidAsync(CommissionMarkPaidDto dto);

    // תצוגה מקדימה של מייל הדוח - בלי לשלוח ובלי לסמן כנשלח
    Task<CommissionEmailPreviewDto> PreviewReportAsync(string ownerId, string month);

    // שולח למשכירה את הדוח החודשי במייל (ומסמן שנשלח). מחזיר false אם אין עמלה לחודש
    Task<bool> SendReportAsync(string ownerId, string month);

    // חלון העלאת האסמכתא מהקישור במייל: אימות הטוקן, תצוגת הסכום, והעלאת הקובץ. null/false = קישור לא תקף
    Task<CommissionProofViewDto?> GetProofViewAsync(string ownerId, string month, string? token);
    Task<bool> SubmitProofAsync(string ownerId, string month, string? token, CommissionProofUploadDto dto);

    // לצפייה של מנהלת באסמכתא שהועלתה
    Task<PaymentProofDto?> GetProofAsync(string ownerId, string month);

    // תשלום באשראי מהקישור במייל: אימות הטוקן ויצירת דף תשלום חדש בסאמיט עם הסכום המדויק. null = קישור לא תקף
    // consent: חובה true - הסכמה לשמירת הכרטיס ולחיוב חודשי אוטומטי
    Task<string?> BeginCardPaymentAsync(string ownerId, string month, string? token, bool consent);

    // אחרי החזרה מדף התשלום: מאמת מול סאמיט שהתשלום תקף ובסכום המלא, ורק אז מסמן את הדוח כשולם
    Task<CommissionCardConfirmResultDto> ConfirmCardPaymentAsync(CommissionCardConfirmDto dto);

    // "שלחו לי מייל" מעמוד התשלום: שולח שוב את הדוח החודשי (עם פרטי ההעברה הבנקאית), בלי חיוב. false = קישור לא תקף
    Task<bool> ResendReportAsync(string ownerId, string month, string? token);

    // שליחה אוטומטית לכל המשכירות שיש להן עמלה ועוד לא נשלח להן דוח לחודש - לשימוש שירות הרקע
    Task SendMonthlyReportsAsync(string month);
}
