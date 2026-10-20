namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IAdminService
{
    Task<AdminOverviewDto> GetOverviewAsync();

    Task<AdminNeighborhoodsDto> GetNeighborhoodsAsync();

    Task SetNeighborhoodAsync(string city, string street, string? neighborhood);

    Task<IReadOnlyList<AdminItemDto>> GetItemsAsync();

    Task<IReadOnlyList<AdminLoanDto>> GetLoansAsync();

    Task DeleteLoanAsync(string id);

    Task<IReadOnlyList<AdminFavoriteDto>> GetFavoritesAsync();
}
