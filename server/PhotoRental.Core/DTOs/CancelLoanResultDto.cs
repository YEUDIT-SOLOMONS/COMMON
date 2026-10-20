namespace PhotoRental.Core.DTOs;

public class CancelLoanResultDto
{
    public int CancelledCount { get; set; }
    public string RefundNote { get; set; } = string.Empty;
}
