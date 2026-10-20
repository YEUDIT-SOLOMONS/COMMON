namespace PhotoRental.Service.Seeding;

using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// זריעה חד-פעמית (ואידמפוטנטית) של רשימת הקטגוריות הקבועה של האתר. נועדה לרוץ ברקע בעליית
// השרת (ראו הקריאה ב-Program.cs) מבלי לחסום את עליית ה-API - כדי לשמור על אותו עיקרון "לא
// לתלות את הרצת השרת בזמינות Firebase" שכבר קיים ב-FirebaseDbContext (מומש בגלל בעיית
// חיבור שגרמה בעבר לקריסת השרת בעליה). אם קטגוריה מהרשימה כבר קיימת (השוואה לפי שם, ללא
// תלות ברישיות) - היא לא נוצרת שוב, כך שאפשר להריץ את הזריעה בכל עליית שרת בבטחה.
public class CategorySeeder
{
    // רשימת הקטגוריות הקבועה של האתר. אין נקודת קצה ציבורית ליצירת קטגוריה (ראו CategoriesController) -
    // זו הדרך היחידה שבה קטגוריות נוצרות.
    public static readonly IReadOnlyList<string> FixedCategoryNames = new[]
    {
        "מצלמות",
        "עדשות",
        "חצובות מצלמה",
        "תאורות",
        "מכונת עשן וערפל",
        "ציוד סאונד",
        "רחפן וציוד רחפן",
        "רקעים",
    };

    private readonly ICategoryRepository _categoryRepository;

    public CategorySeeder(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task SeedAsync()
    {
        var existing = await _categoryRepository.GetAllAsync();

        // מנהלות מנהלות את הקטגוריות במסך הניהול (/admin/categories) - לכן זורעים את הרשימה ההתחלתית
        // רק כשהאוסף ריק לגמרי, כדי שמחיקה או שינוי שם לא יבוטלו בכל עליית שרת
        if (existing.Count > 0)
            return;

        var existingNames = new HashSet<string>(existing.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var name in FixedCategoryNames)
        {
            if (!existingNames.Contains(name))
            {
                await _categoryRepository.CreateAsync(new Category { Name = name, Order = Array.IndexOf(FixedCategoryNames.ToArray(), name) });
            }
        }
    }
}
