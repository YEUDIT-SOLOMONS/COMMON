namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface ICommissionStatementRepository
{
    Task<IReadOnlyList<CommissionStatement>> GetAllAsync();

    // יוצר מסמך חדש או מעדכן את הקיים (לפי בעלים + חודש)
    Task<CommissionStatement> UpsertAsync(CommissionStatement statement);
}
