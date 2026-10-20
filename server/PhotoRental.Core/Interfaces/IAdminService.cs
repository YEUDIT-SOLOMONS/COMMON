namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IAdminService
{
    Task<AdminOverviewDto> GetOverviewAsync();
}
