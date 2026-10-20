namespace PhotoRental.Service.Seeding;

using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Security;

// נתוני דמו לפיתוח מקומי: משתמשת לכל role ופריטים להשכרה, כדי שהמערכת תעבוד מיד אחרי הקמת
// בסיס הנתונים. רץ רק כש-Seed:DemoData=true (מוגדר ב-appsettings.Development.json) ורק פעם אחת
public class DemoDataSeeder
{
    public const string DemoPassword = "Demo1234";

    public static readonly (string Email, string Name, UserRole Role)[] DemoUsers =
    {
        ("admin@demo.local", "מנהלת דמו", UserRole.Admin),
        ("owner@demo.local", "משכירה דמו", UserRole.Owner),
        ("renter@demo.local", "שוכרת דמו", UserRole.Renter),
        ("renter2@demo.local", "שוכרת שנייה", UserRole.Renter),
    };

    private readonly IUserRepository _users;
    private readonly IItemRepository _items;
    private readonly ICategoryRepository _categories;

    public DemoDataSeeder(IUserRepository users, IItemRepository items, ICategoryRepository categories)
    {
        _users = users;
        _items = items;
        _categories = categories;
    }

    public async Task<bool> SeedAsync()
    {
        if (await _users.GetByEmailAsync(DemoUsers[0].Email) is not null)
            return false;

        var now = DateTime.UtcNow;
        string? ownerId = null;
        foreach (var (email, name, role) in DemoUsers)
        {
            var user = await _users.CreateAsync(new User
            {
                Email = email,
                Name = name,
                Role = role,
                Phone = "050" + (1000000 + Array.FindIndex(DemoUsers, u => u.Email == email)).ToString(),
                PasswordHash = PasswordHasher.Hash(DemoPassword),
                CreatedAtUtc = now,
                TermsAcceptedAtUtc = now,
                Address = "הרצל 10",
                City = "ירושלים"
            });
            if (role == UserRole.Owner)
                ownerId = user.Id;
        }

        var categories = (await _categories.GetAllAsync()).ToDictionary(c => c.Name, c => c.Id);
        string? Cat(string name) => categories.GetValueOrDefault(name);

        var demoItems = new (string Name, string Category, decimal Price, int Quantity)[]
        {
            ("Canon EOS R6", "מצלמות", 180, 1),
            ("Sony A7 III", "מצלמות", 160, 1),
            ("עדשה 50mm f/1.8", "עדשות", 40, 2),
            ("עדשה 24-70mm f/2.8", "עדשות", 90, 1),
            ("חצובה Manfrotto", "חצובות מצלמה", 30, 3),
            ("פנס LED Godox", "תאורות", 60, 2),
            ("סופטבוקס 90 ס\"מ", "תאורות", 35, 2),
            ("מיקרופון Rode", "ציוד סאונד", 45, 1),
            ("רחפן DJI Mini 3", "רחפן וציוד רחפן", 200, 1),
            ("רקע לבן 3x6", "רקעים", 25, 1),
            ("מכונת עשן", "מכונת עשן וערפל", 70, 1),
            ("Nikon Z6", "מצלמות", 150, 1),
            ("עדשה 85mm f/1.4", "עדשות", 70, 1),
            ("רקע ירוק (Chroma)", "רקעים", 30, 1),
        };

        var minute = 0;
        foreach (var (name, category, price, quantity) in demoItems)
        {
            await _items.CreateAsync(new Item
            {
                Name = name,
                Description = $"{name} - פריט דמו לבדיקת המערכת",
                DailyPrice = price,
                Quantity = quantity,
                CategoryId = Cat(category),
                OwnerId = ownerId,
                City = "ירושלים",
                Address = "הרצל 10",
                Status = CopyStatus.Available,
                CreatedAtUtc = now.AddMinutes(-minute++),
                Version = 0
            });
        }

        return true;
    }
}
