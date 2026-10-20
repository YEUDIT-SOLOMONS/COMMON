namespace PhotoRental.Core.Interfaces;

using PhotoRental.Core.DTOs;

// פריטים שלא הושכרו 6 שבועות: מייל לבעלים "האם הפריט עדיין תקין וזמין?" עם קישורים ל"זמין" ול"להסרה מהאתר"
public interface IStaleItemService
{
    // סבב בדיקה אחד: שולח מיילים לפריטים שמתאימים. מחזיר כמה מיילים נשלחו
    Task<int> SendNoticesAsync();

    Task<bool> ValidateTokenAsync(string itemId, string? token);
    Task<ItemActionDto?> GetActionViewAsync(string itemId);

    // הבעלים אישרה שהפריט זמין - מאפס את מניין 6 השבועות
    Task ConfirmAvailableAsync(string itemId);

    // הסרת הפריט מהאתר (נחסם אם יש השכרה פעילה)
    Task RemoveAsync(string itemId);
}
