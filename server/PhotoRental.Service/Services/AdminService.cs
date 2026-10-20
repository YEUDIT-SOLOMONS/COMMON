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

    public AdminService(IUserRepository userRepository, IItemRepository itemRepository, ILoanRepository loanRepository)
    {
        _userRepository = userRepository;
        _itemRepository = itemRepository;
        _loanRepository = loanRepository;
    }

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
