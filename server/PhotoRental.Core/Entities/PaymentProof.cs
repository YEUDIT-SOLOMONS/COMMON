namespace PhotoRental.Core.Entities;

// אסמכתת תשלום בהעברה בנקאית שהשוכרת העלתה (תמונה או PDF) - נשמרת בנפרד מההשכרה (אוסף PaymentProofs),
// כדי שרשימות ההשכרות לא יגררו קבצים כבדים. ההשכרה מצביעה אליה דרך Loan.PaymentProofId.
public class PaymentProof
{
    public string? Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    // הקובץ כ-Data URL (base64)
    public string DataUrl { get; set; } = string.Empty;
    public DateTime UploadedAtUtc { get; set; }
}
