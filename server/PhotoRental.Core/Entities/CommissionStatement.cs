namespace PhotoRental.Core.Entities;

// מצב הגבייה של דוח עמלה חודשי למשכירה אחת: האם נשלח אליה ואם שולם. הסכומים עצמם מחושבים תמיד
// מההשכרות שהוחזרו באותו חודש (ראו CommissionService) - כאן נשמר רק המצב.
public class CommissionStatement
{
    public string? Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;

    // "yyyy-MM"
    public string Month { get; set; } = string.Empty;

    public bool Paid { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }

    // אסמכתת העברה שהמשכירה העלתה (אופציונלי) דרך הקישור במייל הדוח - הטוקן הסודי נוצר בשליחת הדוח
    public string? ProofToken { get; set; }
    public string? ProofId { get; set; }
    public DateTime? ProofUploadedAtUtc { get; set; }

    // חיוב אוטומטי של הכרטיס השמור (נעשה פעם אחת לכל דוח, בשליחתו) - נשמר לפני החיוב כדי שלעולם לא יחויב פעמיים
    public DateTime? AutoChargeAttemptedAtUtc { get; set; }

    // הודעות על חוב פתוח: תזכורת (7 ימים אחרי השליחה) והודעת חסימה (14 ימים) - פעם אחת לכל דוח
    public DateTime? DebtWarningSentAtUtc { get; set; }
    public DateTime? DebtBlockedNoticeSentAtUtc { get; set; }

    // קיזוז מתשלומי אשראי שמגיעים למשכירה: כמה מהעמלה קוזז, ומתי הקיזוז נסגר בהעברה בפועל
    public decimal OffsetAmount { get; set; }
    public DateTime? OffsetSettledAtUtc { get; set; }
}
