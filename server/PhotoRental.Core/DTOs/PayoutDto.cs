namespace PhotoRental.Core.DTOs;

public class PayoutRentalDto
{
    public string LoanId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public DateTime ReturnedAt { get; set; }
    public DateTime ReleaseAt { get; set; }
    public decimal Gross { get; set; }
    public decimal Commission { get; set; }
    public decimal Net { get; set; }
    public decimal LateFeeUncollected { get; set; }
    public bool Released { get; set; }
    public bool Paid { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool HasProof { get; set; }
}

public class OwnerPayoutDto
{
    public string OwnerId { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerPhone { get; set; } = string.Empty;
    public bool BankConfigured { get; set; }
    public string? BankHolder { get; set; }
    public string? BankName { get; set; }
    public string? BankBranch { get; set; }
    public string? BankAccount { get; set; }
    public decimal DueNow { get; set; }
    public decimal Pending { get; set; }
    public List<PayoutRentalDto> Rentals { get; set; } = new();

    // עמלות חודשיות שקוזזו מהתשלום (מהשכרות במזומן/בהעברה) - מנוכות מההעברה הבאה
    public List<PayoutOffsetDto> Offsets { get; set; } = new();
    public decimal TransferNow { get; set; }
}

public class PayoutOffsetDto
{
    public string Month { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public class MarkPayoutPaidDto
{
    public string OwnerId { get; set; } = string.Empty;
    public List<string> LoanIds { get; set; } = new();

    // אסמכתת ההעברה (תמונה / PDF כ-Data URL) - אופציונלי
    public string? ProofDataUrl { get; set; }
    public string? ProofFileName { get; set; }
}
