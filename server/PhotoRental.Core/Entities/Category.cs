namespace PhotoRental.Core.Entities;


public class Category
{
    public string? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // סדר ההצגה בקטלוג (נמוך = ראשון) - נקבע ע"י מנהלת במסך הניהול
    public int Order { get; set; }

    // תת-קטגוריה: מזהה קטגוריית האב (ריק = קטגוריה ראשית). עומק אחד בלבד - לתת-קטגוריה אין תתי-קטגוריות.
    public string? ParentId { get; set; }

    // קשר של אחד-לרבים: קטגוריה מכילה פריטים רבים.
    // הרשימה לא נשמרת ישירות במסמך הקטגוריה ב-Firestore (כדי למנוע כפילות וניתוק נתונים),
    // אלא מאוכלסת בשכבת השירות (Service) בעת הצורך בלבד.
    public ICollection<Item> Items { get; set; } = new List<Item>();
}
