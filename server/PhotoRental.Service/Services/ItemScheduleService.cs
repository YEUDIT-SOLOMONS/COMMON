namespace PhotoRental.Service.Services;

using System.Globalization;
using AutoMapper;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

public class ItemScheduleService : IItemScheduleService
{
    private readonly IMapper _mapper;
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(400);

    private readonly ILoanRepository _loans;
    private readonly IItemRepository _items;
    private readonly IItemBlockRepository _blocks;

    public ItemScheduleService(ILoanRepository loans, IItemRepository items, IItemBlockRepository blocks, IMapper mapper)
    {
        _mapper = mapper;
        _loans = loans;
        _items = items;
        _blocks = blocks;
    }

    // תחילת ההשכרה (שעון קיר): תאריך + שעת התחלה
    public static DateTime StartOf(Loan loan)
    {
        var time = !string.IsNullOrWhiteSpace(loan.StartTime) &&
                   TimeSpan.TryParse(loan.StartTime, CultureInfo.InvariantCulture, out var t) ? t : TimeSpan.Zero;
        return DateTime.SpecifyKind(loan.StartDate.Date + time, DateTimeKind.Unspecified);
    }

    // סוף ההשכרה: מועד ההחזרה, ואם עבר והפריט טרם הוחזר - עכשיו (הפריט עדיין אצל השוכרת)
    public static DateTime EndOf(Loan loan, DateTime nowLocal) =>
        loan.ExpectedReturnDate < nowLocal ? nowLocal : loan.ExpectedReturnDate;

    private async Task<List<Loan>> OpenLoansForItemAsync(string itemId)
    {
        var active = await _loans.GetActiveLoansForItemAsync(itemId);
        var overdue = (await _loans.GetByStatusAsync(LoanStatus.Overdue)).Where(l => l.ItemCopyId == itemId);
        return active.Concat(overdue).Where(l => l.CancelledAtUtc is null).ToList();
    }

    // הכי הרבה יחידות שתפוסות בו-זמנית בטווח [start, end)
    public static int PeakUnits(IEnumerable<(DateTime From, DateTime To, int Units)> bookings, DateTime start, DateTime end)
    {
        var events = new List<(DateTime At, int Delta)>();
        foreach (var b in bookings)
        {
            if (b.From >= end || b.To <= start)
                continue;
            events.Add((b.From < start ? start : b.From, b.Units));
            events.Add((b.To > end ? end : b.To, -b.Units));
        }
        int current = 0, peak = 0;
        foreach (var e in events.OrderBy(e => e.At).ThenBy(e => e.Delta))
        {
            current += e.Delta;
            peak = Math.Max(peak, current);
        }
        return peak;
    }

    // טווחים שבהם כל העותקים תפוסים
    private static List<BusyRangeDto> FullRanges(List<(DateTime From, DateTime To, int Units)> bookings, int quantity)
    {
        var result = new List<BusyRangeDto>();
        var events = bookings.SelectMany(b => new[] { (At: b.From, Delta: b.Units), (At: b.To, Delta: -b.Units) })
            .OrderBy(e => e.At).ThenBy(e => e.Delta).ToList();
        var current = 0;
        DateTime? openedAt = null;
        foreach (var e in events)
        {
            current += e.Delta;
            if (current >= quantity && openedAt is null)
                openedAt = e.At;
            else if (current < quantity && openedAt is not null)
            {
                if (e.At > openedAt.Value)
                    result.Add(new BusyRangeDto { From = openedAt.Value, To = e.At });
                openedAt = null;
            }
        }
        return result;
    }

    private static List<(DateTime From, DateTime To, int Units)> Intervals(IEnumerable<Loan> loans, DateTime now) =>
        loans.Select(l => (StartOf(l), EndOf(l, now), l.Units)).ToList();

    public async Task<ItemScheduleDto> GetScheduleAsync(string itemId, string? userId)
    {
        var item = await _items.GetByIdAsync(itemId) ?? throw new KeyNotFoundException("הפריט לא נמצא");
        var now = IsraelTime.Now;
        var isOwner = !string.IsNullOrEmpty(userId) && item.OwnerId == userId;
        var quantity = Math.Max(1, item.Quantity);
        var dto = new ItemScheduleDto { IsOwner = isOwner, Quantity = quantity };

        var relevant = (await OpenLoansForItemAsync(itemId)).OrderBy(StartOf)
            .Where(l => EndOf(l, now) >= now.Date && StartOf(l) <= now + Horizon)
            .ToList();
        foreach (var loan in relevant)
        {
            var from = StartOf(loan);
            var to = EndOf(loan, now);
            dto.Bookings.Add(new BookingDto { From = from, To = to, Units = loan.Units });
            if (isOwner)
            {
                dto.Rentals.Add(new ScheduleRentalDto
                {
                    LoanId = loan.Id ?? string.Empty,
                    Units = loan.Units,
                    From = from,
                    To = to
                });
            }
        }
        dto.Busy.AddRange(FullRanges(Intervals(relevant, now), quantity));

        foreach (var block in (await _blocks.GetByItemAsync(itemId)).Where(b => b.To >= now.Date).OrderBy(b => b.From))
        {
            var blocked = new BusyRangeDto { From = block.From.Date, To = block.To.Date.AddDays(1).AddMinutes(-1) };
            dto.Busy.Add(blocked);
            dto.Blocked.Add(blocked);
            if (isOwner)
                dto.Blocks.Add(_mapper.Map<ItemBlockDto>(block));
        }

        if (item.Status == CopyStatus.Maintenance)
        {
            var maintenance = new BusyRangeDto { From = now.Date, To = now.Date.Add(Horizon) };
            dto.Busy.Add(maintenance);
            dto.Blocked.Add(maintenance);
        }

        dto.Busy = dto.Busy.OrderBy(b => b.From).ToList();
        return dto;
    }

    public async Task EnsureFreeAsync(Item item, DateTime startLocal, DateTime endLocal, int units = 1)
    {
        if (item.Status == CopyStatus.Maintenance)
            throw new InvalidOperationException($"הפריט \"{item.Name}\" אינו זמין להשכרה כרגע");
        if (item.ForSale)
            throw new InvalidOperationException($"הפריט \"{item.Name}\" מוצע למכירה ולא להשכרה");

        var quantity = Math.Max(1, item.Quantity);
        if (units > quantity)
            throw new InvalidOperationException($"יש רק {quantity} יחידות של \"{item.Name}\"");

        var now = IsraelTime.Now;
        var free = quantity - PeakUnits(Intervals(await OpenLoansForItemAsync(item.Id!), now), startLocal, endLocal);
        if (free < units)
            throw new InvalidOperationException(free <= 0 || quantity == 1
                ? $"הפריט \"{item.Name}\" כבר מושכר בחלק מהתאריכים שבחרת. אפשר לראות את התאריכים הפנויים בלוח הזמנים של הפריט"
                : $"בחלק מהתאריכים שבחרת נשארו רק {free} יחידות פנויות של \"{item.Name}\"");

        if ((await _blocks.GetByItemAsync(item.Id!)).Any(b => startLocal.Date <= b.To.Date && endLocal.Date >= b.From.Date))
            throw new InvalidOperationException($"הפריט \"{item.Name}\" אינו זמין בחלק מהתאריכים שבחרת. אפשר לראות את התאריכים הפנויים בלוח הזמנים של הפריט");
    }

    public async Task RefreshStatusAsync(string itemId, int? newRentalCount = null)
    {
        var item = await _items.GetByIdAsync(itemId);
        if (item is null || item.Status == CopyStatus.Maintenance)
        {
            if (item is not null && newRentalCount is not null)
                await _items.PatchRentalCountAsync(itemId, newRentalCount.Value);
            return;
        }

        var now = IsraelTime.Now;
        // "מושכר" רק כשכל העותקים אצל שוכרות כרגע
        var unitsOut = (await OpenLoansForItemAsync(itemId)).Where(l => StartOf(l) <= now).Sum(l => l.Units);
        var status = unitsOut >= Math.Max(1, item.Quantity) ? CopyStatus.Rented : CopyStatus.Available;
        if (status != item.Status || newRentalCount is not null)
            await _items.UpdateStatusAsync(itemId, status, newRentalCount);
    }

    // לשירות הרקע: השכרה עתידית שהתחילה - הפריט הופך ל"מושכר"; השכרה שנסגרה - חוזר ל"זמין"
    public async Task RefreshAllStatusesAsync()
    {
        var itemIds = (await _loans.GetByStatusAsync(LoanStatus.Active))
            .Concat(await _loans.GetByStatusAsync(LoanStatus.Overdue))
            .Select(l => l.ItemCopyId)
            .Concat((await _items.GetAllAsync()).Where(i => i.Status == CopyStatus.Rented).Select(i => i.Id))
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct()
            .ToList();

        foreach (var id in itemIds)
        {
            try { await RefreshStatusAsync(id!); } catch { }
        }
    }

    public async Task<ItemBlockDto> AddBlockAsync(string itemId, string userId, ItemBlockCreateDto dto)
    {
        var item = await _items.GetByIdAsync(itemId) ?? throw new KeyNotFoundException("הפריט לא נמצא");
        if (item.OwnerId != userId)
            throw new UnauthorizedAccessException("אפשר לחסום תאריכים רק לפריט שלך");

        if (!DateTime.TryParseExact(dto.From, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateTime.TryParseExact(dto.To, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            throw new ArgumentException("יש לבחור תאריך התחלה ותאריך סיום");
        if (to < from)
            throw new ArgumentException("תאריך הסיום לפני תאריך ההתחלה");
        if (to < IsraelTime.Now.Date)
            throw new ArgumentException("אי אפשר לחסום תאריכים שכבר עברו");
        var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        if (note is { Length: > 200 })
            throw new ArgumentException("ההערה ארוכה מדי (עד 200 תווים)");

        var now = IsraelTime.Now;
        if ((await OpenLoansForItemAsync(itemId)).Any(l => from <= EndOf(l, now).Date && to >= StartOf(l).Date))
            throw new InvalidOperationException("יש השכרה בחלק מהתאריכים האלה, ולכן אי אפשר לחסום אותם");

        var created = await _blocks.CreateAsync(new ItemBlock
        {
            ItemId = itemId,
            OwnerId = userId,
            From = from,
            To = to,
            Note = note,
            CreatedAtUtc = DateTime.UtcNow
        });
        return _mapper.Map<ItemBlockDto>(created);
    }

    public async Task DeleteBlockAsync(string itemId, string blockId, string userId)
    {
        var block = await _blocks.GetByIdAsync(blockId);
        if (block is null || block.ItemId != itemId)
            throw new KeyNotFoundException("החסימה לא נמצאה");
        if (block.OwnerId != userId)
            throw new UnauthorizedAccessException("אפשר לבטל חסימה רק לפריט שלך");
        await _blocks.DeleteAsync(blockId);
    }

    public async Task<HashSet<string>> GetBlockedItemIdsAsync(DateTime from, DateTime to) =>
        (await _blocks.GetAllAsync())
            .Where(b => from.Date <= b.To.Date && to.Date >= b.From.Date)
            .Select(b => b.ItemId)
            .ToHashSet();
}
