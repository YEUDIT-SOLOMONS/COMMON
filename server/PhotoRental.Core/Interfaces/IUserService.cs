namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync();
    Task<UserDto?> GetByIdAsync(string id);
    Task<UserDto> CreateAsync(UserCreateDto dto);
    Task<UserDto> UpdateAddressAsync(string id, UserAddressUpdateDto dto);
    Task<UserDto> UpdateProfileAsync(string id, UserProfileUpdateDto dto);
    Task<UserDto> AcceptCommissionAsync(string id);
    Task<UserDto> AcceptSaleCommissionAsync(string id);
    Task<UserPayoutInfoDto> GetPayoutAsync(string id);
    Task<UserPayoutInfoDto> UpdatePayoutAsync(string id, UserPayoutUpdateDto dto);
    Task DeleteAsync(string id);
}
