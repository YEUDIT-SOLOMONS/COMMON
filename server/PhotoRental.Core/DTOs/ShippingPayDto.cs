namespace PhotoRental.Core.DTOs;

public class ShippingPayStatusDto
{
    public bool Enabled { get; set; }
    public bool HasCard { get; set; }
}

public class ShippingPayStartDto
{
    public decimal Amount { get; set; }
    public bool Consent { get; set; }

    // פרטי ההזמנה (JSON) - נשמרים בשרת כדי שההזמנה תיפתח גם אם הדפדפן איבד אותם בזמן התשלום
    public string? Draft { get; set; }
    public bool IsBatch { get; set; }
}

public class ShippingPayConfirmDto
{
    public string PaymentId { get; set; } = string.Empty;
    public string? ExternalIdentifier { get; set; }
}

public class ShippingPayConfirmResultDto
{
    public bool Paid { get; set; }
    public string? PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string? Draft { get; set; }
    public bool IsBatch { get; set; }

    // כשהתשלום כבר שימש להזמנה (למשל חזרה שנייה לעמוד האישור) - ההזמנה הקיימת, כדי להעביר אליה במקום לפתוח שוב
    public string? ExistingLoanId { get; set; }
}
