namespace PhotoRental.Service.Services;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;

// נתוני המעקב של מסך הניהול: מחשב סטטיסטיקות לכל משתמש מתוך המשתמשים, הפריטים וההשכרות הקיימים.
// הסכום של השכרה = ימי השכרה x מחיר יומי + עלות משלוח (כמו בכל שאר האתר).
public class AdminService : IAdminService
{
    private readonly IUserRepository _userRepository;
    private readonly IItemRepository _itemRepository;
    private readonly ILoanRepository _loanRepository;
    private readonly IFavoriteRepository _favorites;
    private readonly IStreetNeighborhoodRepository _streets;
    private readonly IItemScheduleService _schedule;

    public AdminService(IUserRepository userRepository, IItemRepository itemRepository, ILoanRepository loanRepository,
        IFavoriteRepository favorites, IStreetNeighborhoodRepository streets, IItemScheduleService schedule)
    {
        _favorites = favorites;
        _streets = streets;
        _schedule = schedule;
        _userRepository = userRepository;
        _itemRepository = itemRepository;
        _loanRepository = loanRepository;
    }

    // כל הרחובות שיש בהם פריטים, והשכונה שנקבעה לכל רחוב (מוצגת בחלון הפריט במקום הרחוב)
    public async Task<AdminNeighborhoodsDto> GetNeighborhoodsAsync()
    {
        var map = await _streets.GetAllAsync();
        var rows = (await _itemRepository.GetAllAsync())
            .Where(i => i.DeletedAtUtc is null && !string.IsNullOrWhiteSpace(i.City))
            .Select(i => (City: i.City!.Trim(), Street: ItemService.StreetOf(i)?.Trim()))
            .Where(r => !string.IsNullOrEmpty(r.Street))
            .GroupBy(r => r)
            .Select(g => new AdminStreetDto
            {
                City = g.Key.City,
                Street = g.Key.Street!,
                Items = g.Count(),
                Neighborhood = map.TryGetValue(Core.Helpers.KnownNeighborhoods.Key(g.Key.City, g.Key.Street!), out var n) ? n : null
            })
            .OrderBy(r => r.Neighborhood is null ? 0 : 1).ThenBy(r => r.City).ThenBy(r => r.Street)
            .ToList();
        return new AdminNeighborhoodsDto { Rows = rows, Options = Core.Helpers.KnownNeighborhoods.ByCity };
    }

    public async Task SetNeighborhoodAsync(string city, string street, string? neighborhood)
    {
        if (string.IsNullOrWhiteSpace(city) || string.IsNullOrWhiteSpace(street))
            throw new ArgumentException("חסרים עיר ורחוב");
        if (neighborhood is { Length: > 60 })
            throw new ArgumentException("שם השכונה ארוך מדי");
        await _streets.SetAsync(city, street, neighborhood);
    }

    // כל הפריטים לשיוך מחדש לקטגוריה
    public async Task<IReadOnlyList<AdminItemDto>> GetItemsAsync()
    {
        var userMap = await UsersByIdAsync();
        return (await _itemRepository.GetAllAsync())
            .Where(i => i.DeletedAtUtc is null && !string.IsNullOrEmpty(i.Id))
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => new AdminItemDto
            {
                Id = i.Id,
                Name = i.Name,
                OwnerName = !string.IsNullOrEmpty(i.OwnerId) && userMap.TryGetValue(i.OwnerId, out var o) ? o.Name : string.Empty,
                CategoryId = i.CategoryId,
                ExtraCategoryId = i.ExtraCategoryId,
                Image = Core.Helpers.ImageLinks.Thumb(i.Id, i.ImageDataUrl),
                CreatedAtUtc = i.CreatedAtUtc
            })
            .ToList();
    }

    public async Task<IReadOnlyList<AdminLoanDto>> GetLoansAsync()
    {
        var itemMap = (await _itemRepository.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var userMap = await UsersByIdAsync();
        string NameOf(string? id) => !string.IsNullOrEmpty(id) && userMap.TryGetValue(id, out var u) ? u.Name : string.Empty;

        return (await _loanRepository.GetAllAsync())
            .OrderByDescending(l => l.LoanDate)
            .Select(l =>
            {
                itemMap.TryGetValue(l.ItemCopyId ?? string.Empty, out var item);
                var renter = !string.IsNullOrEmpty(l.BorrowerId) && userMap.TryGetValue(l.BorrowerId, out var r) ? r : null;
                return new AdminLoanDto
                {
                    Id = l.Id ?? string.Empty,
                    ItemName = item is null ? "(פריט שנמחק)" : Core.Helpers.LoanText.Name(item, l),
                    RenterName = renter?.Name ?? "(משתמשת שנמחקה)",
                    RenterEmail = renter?.Email ?? string.Empty,
                    OwnerName = NameOf(item?.OwnerId),
                    CreatedAt = l.LoanDate,
                    StartDate = l.StartDate,
                    StartTime = l.StartTime,
                    ExpectedReturnDate = DateTime.SpecifyKind(l.ExpectedReturnDate, DateTimeKind.Unspecified),
                    ActualReturnDate = l.ActualReturnDate,
                    Status = l.Status.ToString(),
                    PaymentMethod = l.PaymentMethod.ToString(),
                    PaymentStatus = l.PaymentStatus.ToString(),
                    DeliveryMethod = l.DeliveryMethod.ToString(),
                    Total = (l.RentalDays + l.LateDays) * l.RateOr(item?.DailyPrice ?? 0) * l.Units + l.DeliveryFee + l.ReturnFee
                };
            })
            .ToList();
    }

    // מחיקת השכרה לצמיתות (למשל השכרת ניסיון). לוח הזמנים ומצב הפריט מתעדכנים
    public async Task DeleteLoanAsync(string id)
    {
        var loan = await _loanRepository.GetByIdAsync(id) ?? throw new KeyNotFoundException("ההשכרה לא נמצאה");
        await _loanRepository.DeleteAsync(id);
        Core.Helpers.CatalogCache.Invalidate();
        if (!string.IsNullOrEmpty(loan.ItemCopyId))
        {
            try { await _schedule.RefreshStatusAsync(loan.ItemCopyId); } catch { }
        }
    }

    public async Task<IReadOnlyList<AdminFavoriteDto>> GetFavoritesAsync()
    {
        var all = (await _favorites.GetAllAsync()).Where(f => f.IsFavorite || f.NotifyWhenAvailable).ToList();
        var itemMap = (await _itemRepository.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        var userMap = await UsersByIdAsync();

        return all.GroupBy(f => f.ItemId)
            .Where(g => itemMap.ContainsKey(g.Key) && itemMap[g.Key].DeletedAtUtc is null)
            .Select(g =>
            {
                var item = itemMap[g.Key];
                return new AdminFavoriteDto
                {
                    ItemId = g.Key,
                    ItemName = item.Name,
                    OwnerName = !string.IsNullOrEmpty(item.OwnerId) && userMap.TryGetValue(item.OwnerId, out var owner) ? owner.Name : string.Empty,
                    Status = item.Status.ToString(),
                    Favorites = g.Count(f => f.IsFavorite),
                    Waiting = g.Count(f => f.NotifyWhenAvailable),
                    LastSavedUtc = g.Max(f => f.CreatedAtUtc),
                    Savers = g.OrderByDescending(f => f.CreatedAtUtc)
                        .Select(f => userMap.TryGetValue(f.UserId, out var u)
                            ? new AdminFavoriteSaverDto { Name = u.Name, Email = u.Email, Waiting = f.NotifyWhenAvailable }
                            : new AdminFavoriteSaverDto { Name = "(משתמשת שנמחקה)", Waiting = f.NotifyWhenAvailable })
                        .ToList()
                };
            })
            .OrderByDescending(r => r.Favorites + r.Waiting)
            .ThenByDescending(r => r.LastSavedUtc)
            .ToList();
    }

    private async Task<Dictionary<string, Core.Entities.User>> UsersByIdAsync() =>
        (await _userRepository.GetAllAsync()).Where(u => !string.IsNullOrEmpty(u.Id)).ToDictionary(u => u.Id!);

    public async Task<AdminOverviewDto> GetOverviewAsync()
    {
        var users = await _userRepository.GetAllAsync();
        var items = await _itemRepository.GetAllAsync();
        var loans = await _loanRepository.GetAllAsync();

        var itemsById = items.Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!);
        // פריטים שנמחקו נשארים למחשבון ההכנסות וההיסטוריה (itemsById), אבל לא נספרים כפריטים בקטלוג
        var liveItems = items.Where(i => i.DeletedAtUtc is null).ToList();
        decimal Amount(Core.Entities.Loan loan) =>
            (loan.RentalDays + loan.LateDays) * (itemsById.TryGetValue(loan.ItemCopyId ?? string.Empty, out var item) ? loan.RateOr(item.DailyPrice) : 0) * loan.Units + loan.DeliveryFee;
        string? OwnerOf(Core.Entities.Loan loan) =>
            itemsById.TryGetValue(loan.ItemCopyId ?? string.Empty, out var item) ? item.OwnerId : null;

        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var stats = users.Select(user =>
        {
            var id = user.Id ?? string.Empty;
            var asRenter = loans.Where(l => l.BorrowerId == id).ToList();
            var onTheirItems = loans.Where(l => OwnerOf(l) == id).ToList();

            return new AdminUserStatsDto
            {
                Id = id,
                Name = user.Name,
                Email = user.Email,
                Phone = user.Phone,
                Role = user.Role.ToString(),
                City = user.City,
                CreatedAtUtc = user.CreatedAtUtc,
                LastLoginUtc = user.LastLoginUtc,
                ItemsCount = liveItems.Count(i => i.OwnerId == id),
                RentalsAsRenter = asRenter.Count,
                RentalsOnTheirItems = onTheirItems.Count,
                ActiveRentals = asRenter.Count(l => l.Status == LoanStatus.Active) + onTheirItems.Count(l => l.Status == LoanStatus.Active),
                TotalSpent = asRenter.Sum(Amount),
                TotalEarned = onTheirItems.Sum(Amount)
            };
        })
        .OrderByDescending(u => u.LastLoginUtc ?? u.CreatedAtUtc ?? DateTime.MinValue)
        .ToList();

        return new AdminOverviewDto
        {
            TotalUsers = users.Count,
            NewUsersLast7Days = users.Count(u => u.CreatedAtUtc >= weekAgo),
            ActiveUsersLast7Days = users.Count(u => u.LastLoginUtc >= weekAgo),
            TotalItems = liveItems.Count,
            AvailableItems = liveItems.Count(i => i.Status == CopyStatus.Available),
            TotalLoans = loans.Count,
            ActiveLoans = loans.Count(l => l.Status == LoanStatus.Active),
            PaidLoans = loans.Count(l => l.PaymentStatus == PaymentStatus.Paid),
            TotalRevenue = loans.Where(l => l.PaymentStatus == PaymentStatus.Paid).Sum(Amount),
            Users = stats
        };
    }
}
