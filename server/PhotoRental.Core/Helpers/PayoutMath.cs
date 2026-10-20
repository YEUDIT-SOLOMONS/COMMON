namespace PhotoRental.Core.Helpers;

using PhotoRental.Core.Entities;

public static class PayoutMath
{
    public const int HoldDays = 0;

    public static decimal Gross(Loan loan, decimal dailyPrice) => loan.RentalDays * dailyPrice * loan.Units + loan.LateFeeCharged;

    public static decimal Commission(decimal gross, decimal rate) => Math.Round(gross * rate, 2, MidpointRounding.AwayFromZero);

    public static DateTime? ReleaseAt(Loan loan) =>
        loan.ActualReturnDate is { } returned ? returned.AddDays(HoldDays) : null;
}
