namespace PhotoRental.Service.Services;

using AutoMapper;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;

// שכבת הלוגיקה העסקית לניהול משתמשים
public class UserService : IUserService
{
    private readonly IMapper _mapper;
    private readonly IUserRepository _userRepository;
    private readonly IOutboundCallService _calls;

    public UserService(IUserRepository userRepository, IOutboundCallService calls, IMapper mapper)
    {
        _mapper = mapper;
        _userRepository = userRepository;
        _calls = calls;
    }

    public async Task<IReadOnlyList<UserDto>> GetAllAsync()
    {
        var users = await _userRepository.GetAllAsync();
        return users.Select(_mapper.Map<UserDto>).ToList();
    }

    public async Task<UserDto?> GetByIdAsync(string id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        return user is null ? null : _mapper.Map<UserDto>(user);
    }

    public async Task<UserDto> CreateAsync(UserCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם משתמש");
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ArgumentException("יש להזין כתובת אימייל");

        if (await _userRepository.GetByEmailAsync(dto.Email) is not null)
            throw new InvalidOperationException("קיים כבר משתמש עם כתובת אימייל זו");

        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out var role))
            role = UserRole.Renter;

        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email,
            Phone = dto.Phone,
            Role = role
        };

        var created = await _userRepository.CreateAsync(user);
        return _mapper.Map<UserDto>(created);
    }

    public async Task<UserDto> UpdateAddressAsync(string id, UserAddressUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Address))
            throw new ArgumentException("יש להזין כתובת מגורים מלאה");
        if (string.IsNullOrWhiteSpace(dto.City))
            throw new ArgumentException("יש להזין עיר");

        if (string.IsNullOrWhiteSpace(dto.AddressFloor))
            throw new ArgumentException("יש להזין קומה");
        if (string.IsNullOrWhiteSpace(dto.AddressApartment))
            throw new ArgumentException("יש להזין מספר דירה או שם משפחה");

        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("המשתמש לא נמצא");

        var notes = string.IsNullOrWhiteSpace(dto.AddressNotes) ? null : dto.AddressNotes.Trim();
        if (notes is { Length: > 200 })
            throw new ArgumentException("הערות הכתובת ארוכות מדי (עד 200 תווים)");

        var floor = dto.AddressFloor.Trim();
        var apartment = dto.AddressApartment.Trim();
        var street = string.IsNullOrWhiteSpace(dto.Street) ? null : dto.Street.Trim();
        if (street is { Length: > 100 })
            throw new ArgumentException("שם הרחוב ארוך מדי");
        await _userRepository.UpdateAddressAsync(id, dto.Address, dto.City, notes, floor, apartment, street);

        user.Address = dto.Address;
        user.City = dto.City;
        user.AddressNotes = notes;
        user.AddressFloor = floor;
        user.AddressApartment = apartment;
        user.AddressStreet = street;
        return _mapper.Map<UserDto>(user);
    }

    // אישור תנאי עמלת השירות (פעם אחת, בעת הוספת הפריט הראשון) - נשמר עם חותמת זמן
    public async Task<UserDto> AcceptCommissionAsync(string id)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("המשתמש לא נמצא");

        var now = DateTime.UtcNow;
        await _userRepository.UpdateCommissionConsentAsync(id, now);
        user.CommissionConsentAtUtc = now;
        return _mapper.Map<UserDto>(user);
    }

    public async Task<UserDto> AcceptSaleCommissionAsync(string id)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("המשתמש לא נמצא");

        var now = DateTime.UtcNow;
        await _userRepository.UpdateSaleCommissionConsentAsync(id, now);
        user.SaleCommissionConsentAtUtc = now;
        return _mapper.Map<UserDto>(user);
    }

    public async Task<UserDto> UpdateProfileAsync(string id, UserProfileUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם מלא");

        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("המשתמש לא נמצא");

        await _userRepository.UpdateProfileAsync(id, dto.Name, dto.Phone);
        if (dto.QuietHoursEnabled is { } quiet)
        {
            user.QuietHoursEnabled = quiet;
            user.QuietFrom = QuietHours.Normalize(dto.QuietFrom) ?? QuietHours.DefaultFrom;
            user.QuietTo = QuietHours.Normalize(dto.QuietTo) ?? QuietHours.DefaultTo;
            await _userRepository.UpdateQuietHoursAsync(id, user.QuietHoursEnabled, user.QuietFrom, user.QuietTo);
        }

        var phoneChanged = !string.IsNullOrWhiteSpace(dto.Phone) && dto.Phone.Trim() != (user.Phone ?? string.Empty).Trim();
        user.Name = dto.Name;
        user.Phone = dto.Phone;

        // טלפון ראשון (חשבון גוגל) או מספר חדש - שיחת היכרות למספר החדש
        if (phoneChanged)
        {
            try { await _calls.CallWelcomeAsync(user); } catch { }
        }

        return _mapper.Map<UserDto>(user);
    }

    public async Task<UserPayoutInfoDto> GetPayoutAsync(string id)
    {
        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("המשתמש לא נמצא");

        return MapToPayoutDto(user);
    }

    public async Task<UserPayoutInfoDto> UpdatePayoutAsync(string id, UserPayoutUpdateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.AccountHolderName))
            throw new ArgumentException("יש להזין את שם בעל/ת החשבון");
        if (string.IsNullOrWhiteSpace(dto.BankName))
            throw new ArgumentException("יש להזין את שם הבנק");
        if (string.IsNullOrWhiteSpace(dto.BranchNumber))
            throw new ArgumentException("יש להזין מספר סניף");
        if (string.IsNullOrWhiteSpace(dto.AccountNumber))
            throw new ArgumentException("יש להזין מספר חשבון");
        if (!dto.ConsentAccepted)
            throw new ArgumentException("יש לאשר את שמירת פרטי החשבון ואת הצגתם לשוכר בתשלום בהעברה בנקאית");

        var user = await _userRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("המשתמש לא נמצא");

        await _userRepository.UpdatePayoutAsync(
            id, dto.AccountHolderName, dto.BankName, dto.BranchNumber, dto.AccountNumber, DateTime.UtcNow);

        user.PayoutAccountHolderName = dto.AccountHolderName;
        user.PayoutBankName = dto.BankName;
        user.PayoutBranchNumber = dto.BranchNumber;
        user.PayoutAccountNumber = dto.AccountNumber;

        return MapToPayoutDto(user);
    }

    public async Task DeleteAsync(string id)
    {
        await _userRepository.DeleteAsync(id);
    }

    internal static UserPayoutInfoDto MapToPayoutDto(User user) => new()
    {
        IsConfigured = !string.IsNullOrWhiteSpace(user.PayoutAccountNumber),
        AccountHolderName = user.PayoutAccountHolderName ?? string.Empty,
        BankName = user.PayoutBankName ?? string.Empty,
        BranchNumber = user.PayoutBranchNumber ?? string.Empty,
        AccountNumber = user.PayoutAccountNumber ?? string.Empty
    };
}
