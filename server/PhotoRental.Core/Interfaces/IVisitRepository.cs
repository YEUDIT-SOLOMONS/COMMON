namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IVisitRepository
{
    Task RecordAsync(string day, bool newVisitor);
    Task<IReadOnlyList<VisitDayDto>> GetAllAsync();
}
