namespace PhotoRental.Core.Exceptions;

// נזרקת כשמסמך השתנה בין הקריאה לשמירה (המקבילה של DbUpdateConcurrencyException ב-EF Core)
public class ConcurrencyConflictException : Exception
{
    public string? ResourceId { get; }

    public ConcurrencyConflictException(string message, string? resourceId = null, Exception? inner = null)
        : base(message, inner)
    {
        ResourceId = resourceId;
    }
}
