namespace PhotoRental.Service.Services;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// שכבת הלוגיקה העסקית לניהול פריטי ציוד צילום
public class ItemService : IItemService
{
    private readonly IItemRepository _itemRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly ClientOptions _clientOptions;
    private readonly ILoanRepository _loanRepository;
    private readonly ICommissionDebtService _debtService;

    public ItemService(
        IItemRepository itemRepository,
        ICategoryRepository categoryRepository,
        IUserRepository userRepository,
        IEmailService emailService,
        ClientOptions clientOptions,
        ILoanRepository loanRepository,
        ICommissionDebtService debtService,
        IItemScheduleService schedule,
        IStreetNeighborhoodRepository streetNeighborhoods)
    {
        _streetNeighborhoods = streetNeighborhoods;
        _schedule = schedule;
        _debtService = debtService;
        _loanRepository = loanRepository;
        _itemRepository = itemRepository;
        _categoryRepository = categoryRepository;
        _userRepository = userRepository;
        _emailService = emailService;
        _clientOptions = clientOptions;
    }

    private readonly IItemScheduleService _schedule;
    private readonly IStreetNeighborhoodRepository _streetNeighborhoods;

    public async Task<IReadOnlyList<ItemDto>> GetAllAsync()
    {
        var items = await _itemRepository.GetAllAsync();
        // פריטים של משכירה עם חוב עמלה שעבר 14 ימים מוסתרים מהקטלוג עד לסילוק
        var blocked = await _debtService.GetBlockedOwnerIdsAsync();
        return await MapManyAsync(items.Where(i => i.DeletedAtUtc is null && i.SoldAtUtc is null && (i.OwnerId is null || !blocked.Contains(i.OwnerId))), gallery: false);
    }

    public async Task<PagedResultDto<ItemDto>> GetPageAsync(ItemPageQueryDto query, CancellationToken ct = default)
    {
        var (items, total) = await _itemRepository.GetPageAsync(query, ct);
        return new PagedResultDto<ItemDto>
        {
            Items = await MapManyAsync(items, gallery: false),
            Page = query.Page,
            PageSize = query.PageSize,
            TotalCount = total
        };
    }

    public async Task<ItemDto?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var item = await _itemRepository.GetByIdAsync(id, ct);
        if (item is null || item.DeletedAtUtc is not null)
            return null;

        // בחלון הפריט: השכונה שנקבעה לרחוב בלשונית "שכונות" בניהול, ואם לא נקבעה - הרחוב (בלי מספר בית).
        // לא נשלח ברשימות הקטלוג
        var dto = (await MapManyAsync(new[] { item }))[0];
        var street = StreetOf(item);
        if (!string.IsNullOrEmpty(street) &&
            (await _streetNeighborhoods.GetAllAsync()).TryGetValue(KnownNeighborhoods.Key(item.City, street), out var neighborhood))
            dto.Neighborhood = neighborhood;
        else
            dto.Street = street;
        return dto;
    }

    public async Task<IReadOnlyList<ItemDto>> GetByCategoryAsync(string categoryId)
    {
        var items = await _itemRepository.GetByCategoryIdAsync(categoryId);
        var blocked = await _debtService.GetBlockedOwnerIdsAsync();
        return await MapManyAsync(items.Where(i => i.DeletedAtUtc is null && i.SoldAtUtc is null && (i.OwnerId is null || !blocked.Contains(i.OwnerId))), gallery: false);
    }

    public async Task<IReadOnlyList<ItemDto>> GetByOwnerAsync(string ownerId)
    {
        var items = await _itemRepository.GetByOwnerIdAsync(ownerId);
        return await MapManyAsync(items.Where(i => i.DeletedAtUtc is null), gallery: false);
    }

    // ממפה פריטים ל-DTO, ולפריטים שמושכרים כרגע מוסיף את מועד סיום ההשכרה ("יתפנה ב-...") - שעון קיר של ישראל.
    // ההשכרות נקראות פעם אחת, ורק אם יש בכלל פריט מושכר.
    private async Task<IReadOnlyList<ItemDto>> MapManyAsync(IEnumerable<Item> items, bool gallery = true)
    {
        var list = items.ToList();
        var dtos = list.Select(i => MapToDto(i, gallery)).ToList();
        if (!list.Any(i => i.Status == CopyStatus.Rented))
            return dtos;

        // "יתפנה ב-" - מועד ההחזרה של ההשכרה שבעיצומה (לא של הזמנה עתידית)
        var nowLocal = IsraelTime.Now;
        var busyUntilByItem = (await _loanRepository.GetByStatusAsync(LoanStatus.Active))
            .Where(l => !string.IsNullOrEmpty(l.ItemCopyId) && l.CancelledAtUtc is null && ItemScheduleService.StartOf(l) <= nowLocal)
            .GroupBy(l => l.ItemCopyId!)
            .ToDictionary(g => g.Key, g => g.Max(l => l.ExpectedReturnDate));

        foreach (var dto in dtos.Where(d => d.Status == nameof(CopyStatus.Rented)))
        {
            if (busyUntilByItem.TryGetValue(dto.Id, out var until))
                dto.BusyUntil = DateTime.SpecifyKind(until, DateTimeKind.Unspecified);
        }

        return dtos;
    }

    public async Task<ItemDto> CreateAsync(ItemCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם לפריט");
        ValidatePrices(dto.ForSale, dto.DailyPrice, dto.SalePrice);

        // הגנה על גודל מסמך ה-Firestore (מגבלה: 1MB למסמך) - תמונות גדולות מדי עלולות לגרום לכשל שמירה
        var images = NormalizeImages(dto.ImageDataUrls, dto.ImageDataUrl);

        if (!string.IsNullOrEmpty(dto.CategoryId) && await _categoryRepository.GetByIdAsync(dto.CategoryId) is null)
            throw new KeyNotFoundException("הקטגוריה שצוינה לא נמצאה");

        if (string.IsNullOrEmpty(dto.OwnerId))
            throw new ArgumentException("יש לציין את בעל הפריט");

        var owner = await _userRepository.GetByIdAsync(dto.OwnerId)
            ?? throw new KeyNotFoundException("המשתמש (בעל הציוד) שצוין לא נמצא");

        if ((await _debtService.GetBlockedOwnerIdsAsync()).Contains(dto.OwnerId))
            throw new InvalidOperationException("יש לך דוח עמלה שלא שולם מעל 14 ימים, ולכן לא ניתן להוסיף פריטים חדשים כרגע. אפשר לשלם בקישור שבמייל הדוח או באזור האישי, והכול יחזור לפעול מיד.");

        // כתובת מגורים ועיר נאספות פעם אחת מהמשכיר (בפריט הראשון שהוא מוסיף) ומשמשות מאז ואילך
        // למילוי אוטומטי של מיקום כל הפריטים שלו - יש לשמור אותן דרך PATCH /api/users/{id}/address
        // לפני הוספת פריט ראשון.
        if (string.IsNullOrWhiteSpace(owner.Address) || string.IsNullOrWhiteSpace(owner.City))
            throw new ArgumentException("יש להזין כתובת מגורים ועיר לפני הוספת פריט להשכרה");

        if (dto.HourlyPrice.HasValue && dto.HourlyPrice.Value <= 0)
            throw new ArgumentException("מחיר לשעה חייב להיות גדול מאפס");

        if (!Enum.TryParse<RentalUnit>(dto.MinRentalUnit, ignoreCase: true, out var minRentalUnit))
            minRentalUnit = RentalUnit.Days;

        var minRentalAmount = dto.MinRentalAmount > 0 ? dto.MinRentalAmount : 1;
        var isLocation = !dto.ForSale && await IsLocationCategoryAsync(dto.CategoryId);
        if (isLocation)
        {
            if (dto.HourlyPrice is not > 0)
                throw new ArgumentException("לסטודיו ולוקיישן יש להזין מחיר לשעה");
            minRentalUnit = RentalUnit.Hours;
            dto.Quantity = 1;
        }
        if (dto.ForSale)
            dto.Quantity = 1;

        var item = new Item
        {
            Name = dto.Name,
            Description = dto.Description,
            DailyPrice = dto.AddVat ? Vat.Add(dto.DailyPrice) : dto.DailyPrice,
            HourlyPrice = dto.HourlyPrice is { } h ? (dto.AddVat ? Vat.Add(h) : h) : null,
            VatAdded = dto.AddVat,
            Quantity = Math.Clamp(dto.Quantity, 1, 50),
            IsLocation = isLocation,
            ForSale = dto.ForSale,
            SalePrice = dto.ForSale ? Math.Round(dto.SalePrice, 2) : 0,
            SaleCondition = dto.ForSale ? ValidCondition(dto.SaleCondition) : null,
            ExtraCategoryId = await ValidExtraCategoryAsync(dto.ExtraCategoryId, dto.CategoryId),
            MinRentalUnit = minRentalUnit,
            MinRentalAmount = minRentalAmount,
            SerialNumber = dto.SerialNumber,
            CategoryId = dto.CategoryId,
            OwnerId = dto.OwnerId,
            ImageDataUrl = images.FirstOrDefault(),
            ExtraImageDataUrls = images.Skip(1).ToList(),
            City = owner.City,
            Address = owner.Address,
            Street = owner.AddressStreet,
            CreatedAtUtc = DateTime.UtcNow,
            Status = CopyStatus.Available
        };

        var created = await _itemRepository.CreateAsync(item);
        await SendItemAddedEmailAsync(created, owner);
        return MapToDto(created);
    }

    // מייל אישור לבעל הפריט מיד לאחר שהפריט נוסף. תקלה בשליחה לעולם לא מכשילה את הוספת הפריט.
    private async Task SendItemAddedEmailAsync(Item item, User owner)
    {
        if (string.IsNullOrWhiteSpace(owner.Email))
            return;

        try
        {
            var h = (string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var profileLink = $"<a href=\"{_clientOptions.BaseUrl.TrimEnd('/')}/profile\">באזור האישי</a>";
            // הכתובת המלאה של הפריט (רחוב ומספר, ואז העיר) - לא רק העיר
            var fullAddress = string.Join(", ", new[] { item.Address, item.City }.Where(part => !string.IsNullOrWhiteSpace(part)));
            var notesRow = string.IsNullOrWhiteSpace(owner.AddressNotes)
                ? string.Empty
                : $"<tr><td><strong>הערות כתובת</strong></td><td>{h(owner.AddressNotes)}</td></tr>";
            var hourly = item.HourlyPrice.HasValue ? $" (או ₪{item.HourlyPrice.Value:0.##} לשעה)" : string.Empty;
            var conditions = new Dictionary<string, string> { ["New"] = "חדש בקופסה", ["LikeNew"] = "כמו חדש - שומש פעמים בודדות", ["Used"] = "משומש", ["Damaged"] = "קצת פגום" };
            var priceRow = item.ForSale
                ? $"<tr><td><strong>מחיר מכירה</strong></td><td>₪{item.SalePrice:0.##}</td></tr><tr><td><strong>מצב</strong></td><td>{h(conditions.GetValueOrDefault(item.SaleCondition ?? "Used", "משומש"))}</td></tr>"
                : $"<tr><td><strong>מחיר יומי</strong></td><td>₪{item.DailyPrice:0.##}{h(hourly)}</td></tr>";
            var html = $"""
                <div dir="rtl" style="font-family:Arial,sans-serif">
                <p>שלום {h(NameText.FirstName(owner.Name))},</p>
                <p>הפריט <strong>{h(item.Name)}</strong> נוסף בהצלחה {(item.ForSale ? "ללוח יד 2 של common" : "לקטלוג common")}!</p>
                <table style="border-collapse:collapse" cellpadding="6">
                  {priceRow}
                  <tr><td><strong>מיקום</strong></td><td>{h(fullAddress)}</td></tr>{notesRow}
                  <tr><td><strong>סטטוס</strong></td><td>{(item.ForSale ? "למכירה" : "זמין להשכרה")}</td></tr>
                </table>
                <p>{(item.ForSale ? "מעכשיו חברות הקהילה יכולות למצוא אותו ולקנות אותו." : "מעכשיו חברות הקהילה יכולות למצוא אותו ולהזמין אותו.")} אפשר לערוך או למחוק את הפריט בכל עת {profileLink}.</p>
                </div>
                """;
            await _emailService.SendAsync(owner.Email, owner.Name, "הפריט נוסף בהצלחה - common", html);
        }
        catch
        {
            // מייל שלא נשלח אינו סיבה לבטל הוספת פריט שכבר נשמרה
        }
    }

    public async Task<ItemDto> UpdateAsync(string id, ItemUpdateDto dto)
    {
        var item = await _itemRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");

        // רק בעל הפריט רשאי לערוך אותו
        if (!string.IsNullOrEmpty(item.OwnerId) && item.OwnerId != dto.OwnerId)
            throw new ArgumentException("רק בעל הפריט יכול לערוך אותו");

        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם לפריט");
        ValidatePrices(dto.ForSale, dto.DailyPrice, dto.SalePrice);
        if (item.SoldAtUtc is not null)
            throw new InvalidOperationException("הפריט כבר נמכר");
        var existing = string.IsNullOrEmpty(item.ImageDataUrl)
            ? new List<string>()
            : new[] { item.ImageDataUrl }.Concat(item.ExtraImageDataUrls).ToList();
        var images = NormalizeImages(
            dto.ImageDataUrls?.Select(v => ResolveExisting(v, id, existing)).Where(v => v is not null).Select(v => v!).ToList(),
            dto.ImageDataUrl is null ? null : ResolveExisting(dto.ImageDataUrl, id, existing));
        if (dto.HourlyPrice.HasValue && dto.HourlyPrice.Value <= 0)
            throw new ArgumentException("מחיר לשעה חייב להיות גדול מאפס");
        if (!string.IsNullOrEmpty(dto.CategoryId) && await _categoryRepository.GetByIdAsync(dto.CategoryId) is null)
            throw new KeyNotFoundException("הקטגוריה שצוינה לא נמצאה");

        if (!Enum.TryParse<RentalUnit>(dto.MinRentalUnit, ignoreCase: true, out var minRentalUnit))
            minRentalUnit = RentalUnit.Days;

        var isLocation = !dto.ForSale && await IsLocationCategoryAsync(dto.CategoryId);
        if (isLocation)
        {
            if (dto.HourlyPrice is not > 0)
                throw new ArgumentException("לסטודיו ולוקיישן יש להזין מחיר לשעה");
            minRentalUnit = RentalUnit.Hours;
        }

        item.Name = dto.Name;
        item.Description = dto.Description;
        item.DailyPrice = dto.AddVat ? Vat.Add(dto.DailyPrice) : dto.DailyPrice;
        item.HourlyPrice = dto.HourlyPrice is { } hourly ? (dto.AddVat ? Vat.Add(hourly) : hourly) : null;
        item.VatAdded = dto.AddVat;
        item.Quantity = Math.Clamp(dto.Quantity, 1, 50);
        item.IsLocation = isLocation;
        item.ForSale = dto.ForSale;
        item.SalePrice = dto.ForSale ? Math.Round(dto.SalePrice, 2) : 0;
        item.SaleCondition = dto.ForSale ? ValidCondition(dto.SaleCondition) : null;
        if (dto.ForSale) item.Quantity = 1;
        item.ExtraCategoryId = await ValidExtraCategoryAsync(dto.ExtraCategoryId, dto.CategoryId);
        if (isLocation) item.Quantity = 1;
        item.MinRentalUnit = minRentalUnit;
        item.MinRentalAmount = dto.MinRentalAmount > 0 ? dto.MinRentalAmount : 1;
        item.CategoryId = dto.CategoryId;
        item.ImageDataUrl = images.FirstOrDefault();
        item.ExtraImageDataUrls = images.Skip(1).ToList();

        await _itemRepository.UpdateAsync(id, item);
        return MapToDto(item);
    }

    // תמונה קיימת מגיעה מהלקוח כקישור /img/{id}/{n} - מחזירים את התמונה השמורה עצמה; קישור זר נזרק
    private static string? ResolveExisting(string value, string itemId, List<string> existing)
    {
        if (value.StartsWith("data:", StringComparison.Ordinal) || value.StartsWith("http", StringComparison.Ordinal))
            return value;
        return ImageLinks.ExistingIndex(value, itemId) is { } i && i < existing.Count ? existing[i] : null;
    }

    private const int MaxImagesPerItem = 6;
    private const int MaxTotalImageChars = 900_000;

    // רשימת התמונות המנורמלת (הראשונה = ראשית): ImageDataUrls אם נשלח, אחרת ImageDataUrl הבודד.
    // מגביל כמות וגודל כולל כדי לא לחצות את מגבלת 1MB של מסמך ב-Firestore.
    private static List<string> NormalizeImages(List<string>? imageDataUrls, string? singleImageDataUrl)
    {
        var images = (imageDataUrls ?? (string.IsNullOrEmpty(singleImageDataUrl) ? new List<string>() : new List<string> { singleImageDataUrl }))
            .Where(image => !string.IsNullOrWhiteSpace(image))
            .ToList();

        if (images.Count > MaxImagesPerItem)
            throw new ArgumentException($"ניתן להעלות עד {MaxImagesPerItem} תמונות לפריט");
        if (images.Sum(image => image.Length) > MaxTotalImageChars)
            throw new ArgumentException("התמונות גדולות מדי - נא להעלות פחות תמונות או תמונות קטנות יותר");

        return images;
    }

    public async Task DeleteAsync(string id)
    {
        // מחיקה רכה: הפריט מוסתר מהקטלוג, אבל היסטוריית ההשכרות שלו נשמרת לתמיד
        await _itemRepository.SoftDeleteAsync(id, DateTime.UtcNow);
    }

    public async Task<ItemAddressDto> GetAddressAsync(string id)
    {
        var item = await _itemRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");

        // הערות הכתובת נשמרות אצל בעל הפריט (כדי שעדכון שלהן יחול גם על פריטים קיימים)
        var owner = !string.IsNullOrEmpty(item.OwnerId) ? await _userRepository.GetByIdAsync(item.OwnerId) : null;
        return new ItemAddressDto { Address = LoanService.PickupAddressLine(item.Address, owner) ?? item.Address, City = item.City, AddressNotes = owner?.AddressNotes };
    }

    public async Task<UserPayoutInfoDto> GetOwnerPayoutAsync(string id)
    {
        var item = await _itemRepository.GetByIdAsync(id)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");

        if (string.IsNullOrEmpty(item.OwnerId))
            throw new KeyNotFoundException("לפריט זה אין בעלים משויך");

        var owner = await _userRepository.GetByIdAsync(item.OwnerId)
            ?? throw new KeyNotFoundException("בעל הפריט לא נמצא");

        return UserService.MapToPayoutDto(owner);
    }

    public async Task<IReadOnlyList<string>> GetBusyItemIdsAsync(DateTime from, DateTime to)
    {
        var busy = new HashSet<string>();
        var now = IsraelTime.Now;
        var quantities = (await _itemRepository.GetAllAsync()).Where(i => !string.IsNullOrEmpty(i.Id)).ToDictionary(i => i.Id!, i => Math.Max(1, i.Quantity));
        var open = new List<Loan>();
        foreach (var status in new[] { LoanStatus.Active, LoanStatus.Overdue })
            open.AddRange((await _loanRepository.GetByStatusAsync(status)).Where(l => !string.IsNullOrEmpty(l.ItemCopyId) && l.CancelledAtUtc is null));

        // פריט תפוס כשבמשך הטווח כל העותקים שלו מושכרים (השכרה שלא הוחזרה תופסת לפחות עד היום)
        foreach (var group in open.GroupBy(l => l.ItemCopyId!))
        {
            var intervals = group.Select(l => (From: l.StartDate.Date, To: ItemScheduleService.EndOf(l, now).Date.AddDays(1), l.Units));
            if (ItemScheduleService.PeakUnits(intervals, from.Date, to.Date.AddDays(1)) >= quantities.GetValueOrDefault(group.Key, 1))
                busy.Add(group.Key);
        }

        foreach (var item in await _itemRepository.GetAllAsync())
            if (item.Status == CopyStatus.Maintenance && !string.IsNullOrEmpty(item.Id))
                busy.Add(item.Id);

        busy.UnionWith(await _schedule.GetBlockedItemIdsAsync(from, to));
        return busy.ToList();
    }

    private static void ValidatePrices(bool forSale, decimal dailyPrice, decimal salePrice)
    {
        if (forSale && salePrice <= 0)
            throw new ArgumentException("מחיר המכירה חייב להיות גדול מאפס");
        if (!forSale && dailyPrice <= 0)
            throw new ArgumentException("מחיר יומי חייב להיות גדול מאפס");
    }

    private static string ValidCondition(string? condition) =>
        condition is "New" or "LikeNew" or "Used" or "Damaged" ? condition : "Used";

    private static ItemDto MapToDto(Item item, bool gallery = true) => new()
    {
        Id = item.Id ?? string.Empty,
        Name = item.Name,
        Description = item.Description,
        DailyPrice = item.DailyPrice,
        HourlyPrice = item.HourlyPrice,
        AddVat = item.VatAdded,
        Quantity = Math.Max(1, item.Quantity),
        IsLocation = item.IsLocation,
        ForSale = item.ForSale,
        SalePrice = item.SalePrice,
        SaleCondition = item.SaleCondition,
        Sold = item.SoldAtUtc is not null,
        ExtraCategoryId = item.ExtraCategoryId,
        CreatedAtUtc = item.CreatedAtUtc,
        MinRentalUnit = item.MinRentalUnit.ToString(),
        MinRentalAmount = item.MinRentalAmount,
        SerialNumber = item.SerialNumber,
        Status = item.Status.ToString(),
        CategoryId = item.CategoryId,
        OwnerId = item.OwnerId,
        ImageDataUrl = gallery && !string.IsNullOrEmpty(item.ImageDataUrl) && !string.IsNullOrEmpty(item.Id)
            ? ImageLinks.Full(item.Id, 0, item.ImageDataUrl)
            : ImageLinks.Thumb(item.Id, item.ImageDataUrl),
        ImageCount = string.IsNullOrEmpty(item.ImageDataUrl) ? 0 : 1 + item.ExtraImageDataUrls.Count,
        ImageDataUrls = !gallery || string.IsNullOrEmpty(item.ImageDataUrl) || string.IsNullOrEmpty(item.Id)
            ? new List<string>()
            : new[] { item.ImageDataUrl }.Concat(item.ExtraImageDataUrls).Select((d, i) => ImageLinks.Full(item.Id, i, d)).ToList(),
        City = item.City
    };

    // פריטים ישנים שנשמרו לפני שדה הרחוב הנפרד: מורידים רק את מספר הבית שבסוף ("שדרות 7 בנובמבר 12" -> "שדרות 7 בנובמבר")
    private static readonly System.Text.RegularExpressions.Regex TrailingHouseNumber =
        new(@"^(.*\S)\s+\d+[֐-׿A-Za-z/-]*$");

    public async Task SetCategoryAsync(string id, string categoryId, string? extraCategoryId)
    {
        var item = await _itemRepository.GetByIdAsync(id) ?? throw new KeyNotFoundException("הפריט לא נמצא");
        if (string.IsNullOrWhiteSpace(categoryId) || await _categoryRepository.GetByIdAsync(categoryId) is null)
            throw new ArgumentException("יש לבחור קטגוריה קיימת");
        var isLocation = await IsLocationCategoryAsync(categoryId);
        if (isLocation && item.HourlyPrice is not > 0)
            throw new ArgumentException("לסטודיו ולוקיישן צריך מחיר לשעה - המשכירה צריכה להעביר את הפריט בעצמה בעריכה");
        item.CategoryId = categoryId;
        item.ExtraCategoryId = await ValidExtraCategoryAsync(extraCategoryId, categoryId);
        if (isLocation != item.IsLocation)
        {
            item.IsLocation = isLocation;
            if (isLocation)
            {
                item.MinRentalUnit = RentalUnit.Hours;
                item.Quantity = 1;
            }
            else if (item.MinRentalUnit == RentalUnit.Hours)
            {
                item.MinRentalUnit = RentalUnit.Days;
                item.MinRentalAmount = 1;
            }
        }
        await _itemRepository.UpdateAsync(id, item);
        CatalogCache.Invalidate();
    }

    private async Task<string?> ValidExtraCategoryAsync(string? extraId, string? mainId)
    {
        if (string.IsNullOrWhiteSpace(extraId) || extraId == mainId)
            return null;
        // סטודיו/לוקיישן הוא רק להשכרת מקום - לא קטגוריה נוספת לפריט
        if (await _categoryRepository.GetByIdAsync(extraId) is null || await IsLocationCategoryAsync(extraId))
            return null;
        return extraId;
    }

    // קטגוריית סטודיו/לוקיישן (או תת-קטגוריה שלה)
    private async Task<bool> IsLocationCategoryAsync(string? categoryId)
    {
        for (var depth = 0; depth < 3 && !string.IsNullOrEmpty(categoryId); depth++)
        {
            var category = await _categoryRepository.GetByIdAsync(categoryId);
            if (category is null)
                return false;
            if (category.Name.Contains("לוקיישן"))
                return true;
            categoryId = category.ParentId;
        }
        return false;
    }

    public static string? StreetOf(Item item) => item.Street ?? StreetWithoutHouseNumber(item.Address);

    public static string? StreetWithoutHouseNumberPublic(string? address) => StreetWithoutHouseNumber(address);

    internal static string? StreetWithoutHouseNumber(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return null;
        var text = address.Split(',')[0].Trim();
        var match = TrailingHouseNumber.Match(text);
        return match.Success ? match.Groups[1].Value : text;
    }
}
