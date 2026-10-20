namespace PhotoRental.Core.DTOs;

using System.ComponentModel.DataAnnotations;

// מוצג ללקוח כאשר שולפים קטגוריה
public class CategoryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Order { get; set; }
    public string? ParentId { get; set; }
}

// נשלח מהלקוח לשינוי סדר ההצגה של הקטגוריות בקטלוג: מזהי כל הקטגוריות לפי הסדר הרצוי
public class CategoryReorderDto
{
    public List<string> Ids { get; set; } = new();
}

// נשלח מהלקוח ביצירת קטגוריה חדשה
public class CategoryCreateDto
{
    [Required(ErrorMessage = "יש להזין שם קטגוריה")]
    [StringLength(60)]
    public string Name { get; set; } = string.Empty;

    // ריק = קטגוריה ראשית; אחרת מזהה קטגוריית האב (יצירת תת-קטגוריה). בעריכת שם הערך מתעלמים ממנו.
    public string? ParentId { get; set; }
}
