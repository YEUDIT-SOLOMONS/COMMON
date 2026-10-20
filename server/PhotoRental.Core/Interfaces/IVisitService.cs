using PhotoRental.Core.DTOs;

namespace PhotoRental.Core.Interfaces;

public interface IVisitService
{
    Task RecordAsync(bool isNewVisitor, string? userAgent);

    Task<IReadOnlyList<VisitDayDto>> GetAllAsync();
}
