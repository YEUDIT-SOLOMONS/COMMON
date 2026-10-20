namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.Entities;

public interface ICallLogRepository
{
    Task<IReadOnlyList<CallLog>> GetAllAsync();
    Task<CallLog?> GetByIdAsync(string id);
    Task<IReadOnlyList<CallLog>> GetByStatusAsync(string status);
    Task<IReadOnlyList<CallLog>> GetByLoanIdAsync(string loanId);
    Task<IReadOnlyList<CallLog>> GetByUserIdAsync(string userId);
    Task<CallLog> CreateAsync(CallLog log);
    Task UpdateAsync(CallLog log);
}
