namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IPayoutService
{
    Task<IReadOnlyList<OwnerPayoutDto>> GetAsync();

    Task MarkPaidAsync(MarkPayoutPaidDto dto);

    // כמה כסף מהשכרות באשראי ממתין למשכירה (מוכן להעברה, אחרי קיזוזים קודמים)
    Task<decimal> GetCardBalanceAsync(string ownerId);
}
