namespace PhotoRental.Core.Options;

// כתובת בסיס האתר (הלקוח) - משמשת לבניית קישורים מוחלטים בתוך אימיילים (כגון קישור איפוס סיסמה)
public class ClientOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5173";
}
