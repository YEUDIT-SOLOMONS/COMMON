namespace PhotoRental.Core.DTOs;

// שורה בדוח העמלה: השכרה שהוחזרה בחודש הנבחר
public class CommissionRentalDto
{
    public string LoanId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public string? StartTime { get; set; }
    public DateTime ReturnedAt { get; set; }
    public int RentalDays { get; set; }

    // ימי איחור שנוספו (כל יום איחור מחויב כיום השכרה נוסף, והעמלה חלה גם עליו)
    public int LateDays { get; set; }
    public decimal RentalAmount { get; set; }
    public decimal Rate { get; set; }
    public decimal Commission { get; set; }
}

public class CommissionMonthSummaryDto
{
    public string Month { get; set; } = string.Empty;
    public decimal TotalCommission { get; set; }
    public bool Paid { get; set; }
}

// דוח העמלה החודשי של משכירה אחת (האזור האישי והמייל החודשי)
public class CommissionReportDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Percent { get; set; }
    public int TotalRentals { get; set; }
    public decimal TotalRentalAmount { get; set; }
    public decimal TotalCommission { get; set; }
    public bool Paid { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public List<CommissionRentalDto> Rentals { get; set; } = new();
    public List<CommissionMonthSummaryDto> RecentMonths { get; set; } = new();
}

public class CommissionAdminRowDto
{
    public decimal OffsetAmount { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;
    public int TotalRentals { get; set; }
    public decimal TotalRentalAmount { get; set; }
    public decimal TotalCommission { get; set; }
    public bool Paid { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }

    // המשכירה העלתה אסמכתת העברה דרך הקישור במייל
    public bool HasProof { get; set; }
    public DateTime? ProofUploadedAtUtc { get; set; }
    public List<CommissionRentalDto> Rentals { get; set; } = new();
}

public class CommissionAdminOverviewDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Percent { get; set; }
    public decimal TotalCommission { get; set; }
    public decimal PaidCommission { get; set; }
    public decimal UnpaidCommission { get; set; }
    public List<string> Months { get; set; } = new();
    public List<CommissionAdminRowDto> Owners { get; set; } = new();
}

public class CommissionMarkPaidDto
{
    public string OwnerId { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public bool Paid { get; set; }
}

// תצוגה מקדימה של מייל הדוח (בלי לשלוח אותו): למי יישלח, הנושא והתוכן
public class CommissionEmailPreviewDto
{
    public string ToEmail { get; set; } = string.Empty;
    public string ToName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
}

public class CommissionSendDto
{
    public string OwnerId { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
}

// אימות תשלום באשראי אחרי החזרה מדף התשלום של סאמיט (הפרמטרים מגיעים בכתובת ההפניה: og-paymentid, og-externalidentifier)
public class CommissionCardConfirmDto
{
    public string PaymentId { get; set; } = string.Empty;
    public string? ExternalIdentifier { get; set; }
}

public class CommissionCardConfirmResultDto
{
    // true = התשלום אומת מול סאמיט והדוח סומן כשולם
    public bool Paid { get; set; }
    public string? MonthLabel { get; set; }
    public decimal Amount { get; set; }
}
