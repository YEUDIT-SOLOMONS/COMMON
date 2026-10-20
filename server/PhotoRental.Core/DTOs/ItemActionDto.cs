namespace PhotoRental.Core.DTOs;

// מה שמוצג בחלון "זמין / להסרה" שנפתח מהקישור במייל (בלי התחברות)
public class ItemActionDto
{
    public string ItemName { get; set; } = string.Empty;
    public string? ImageDataUrl { get; set; }

    // true כשיש לפריט השכרה פעילה כרגע - אי אפשר להסיר אותו עד שיוחזר
    public bool HasActiveLoan { get; set; }
}
