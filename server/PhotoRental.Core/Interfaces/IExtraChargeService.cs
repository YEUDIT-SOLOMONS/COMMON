namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public class ExtraChargeResult
{
    public bool Success { get; set; }
    public string? PaymentId { get; set; }
    public string? Error { get; set; }
}

// חיוב אוטומטי מהכרטיס השמור של השוכרת על תוספות להשכרה ששולמה באשראי: יום הארכה ואיחור בהחזרה
public interface IExtraChargeService
{
    Task<ExtraChargeResult> ChargeAsync(User payer, decimal amount, string description);
}
