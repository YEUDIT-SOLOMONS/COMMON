namespace PhotoRental.API.Middlewares;

using System.Net;
using System.Text.Json;

// Middleware גלובלי לתפיסת שגיאות בצורה מרוכזת ומסודרת,
// כדי שכל בקר (Controller) לא יצטרך try/catch משלו וכדי שהלקוח תמיד יקבל תשובת JSON עקבית.
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // רישום מלא ליומן (כולל Stack Trace) לכל שגיאה שנתפסת - כתובת, פעולה, ומזהה המשתמש
            // אם קיים (מתוך תוקן ה-JWT) - כדי שכל שגיאה תהיה ניתנת לאיתור ולמעקב בקובצי הלוג.
            var userId = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            // שגיאות עסקיות צפויות (קלט לא תקין, לא נמצא, התחברות שגויה, מצב שלא מאפשר את הפעולה) נרשמות כאזהרה קצרה
            // בלי Stack Trace, כדי שביומן יופיעו כשגיאות (ERROR) רק תקלות אמיתיות
            if (ex is ArgumentException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    "בקשה נדחתה ({ExceptionType}): {Method} {Path} (משתמש: {UserId}) - {Reason}",
                    ex.GetType().Name,
                    context.Request.Method,
                    context.Request.Path,
                    userId ?? "אנונימי",
                    ex.Message);
            }
            else
            {
                _logger.LogError(
                    ex,
                    "שגיאה לא צפויה בעת טיפול בבקשה: {Method} {Path} (משתמש: {UserId})",
                    context.Request.Method,
                    context.Request.Path,
                    userId ?? "אנונימי");
            }

            await HandleExceptionAsync(context, ex, _environment.IsDevelopment());
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception, bool isDevelopment)
    {
        // מיפוי סוגי חריגות נפוצים לקודי סטטוס HTTP מתאימים
        var statusCode = exception switch
        {
            ArgumentException => HttpStatusCode.BadRequest,
            KeyNotFoundException => HttpStatusCode.NotFound,
            InvalidOperationException => HttpStatusCode.Conflict,
            UnauthorizedAccessException => HttpStatusCode.Unauthorized,
            _ => HttpStatusCode.InternalServerError
        };

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        string message;
        if (statusCode == HttpStatusCode.InternalServerError && !isDevelopment)
        {
            // בפרודקשן לא חושפים ללקוח פרטים פנימיים - רק טקסט כללי (הפרטים המלאים נכתבים ללוג)
            message = "אירעה שגיאה בשרת, אנא נסו שוב מאוחר יותר";
        }
        else
        {
            // בסביבת פיתוח (או עבור שגיאות עסקיות צפויות) מציגים את פרטי השגיאה האמיתיים,
            // כולל שרשרת חריגות פנימיות (InnerException) - כדי לאבחן תקלות במהירות בלי לחפש בלוג.
            message = BuildDetailedMessage(exception, isDevelopment);
        }

        var response = new
        {
            StatusCode = (int)statusCode,
            Message = message
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }

    private static string BuildDetailedMessage(Exception exception, bool isDevelopment)
    {
        if (!isDevelopment)
        {
            return exception.Message;
        }

        var parts = new List<string> { exception.Message };
        var inner = exception.InnerException;
        while (inner is not null)
        {
            parts.Add(inner.Message);
            inner = inner.InnerException;
        }

        return string.Join(" <- ", parts);
    }
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionMiddleware>();
    }
}
