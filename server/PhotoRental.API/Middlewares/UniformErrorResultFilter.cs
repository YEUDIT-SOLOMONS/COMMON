using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PhotoRental.API.Middlewares;

// תשובות שגיאה שה-Controllers מחזירים בעצמם (NotFound, Forbid, Conflict...) עוברות לאותו מבנה JSON
// של GlobalExceptionMiddleware: statusCode, message, correlationId
public class UniformErrorResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        var (status, value) = context.Result switch
        {
            ObjectResult o when (o.StatusCode ?? 200) >= 400 => (o.StatusCode!.Value, o.Value),
            StatusCodeResult s when s.StatusCode >= 400 => (s.StatusCode, (object?)null),
            _ => (0, null)
        };
        if (status == 0 || value?.GetType().GetProperty("StatusCode") is not null)
            return;

        var message = value as string
                      ?? value?.GetType().GetProperty("Message")?.GetValue(value) as string
                      ?? DefaultMessage(status);
        context.Result = new ObjectResult(new
        {
            StatusCode = status,
            Message = message,
            CorrelationId = context.HttpContext.GetCorrelationId()
        }) { StatusCode = status };
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    private static string DefaultMessage(int status) => status switch
    {
        401 => "נדרשת התחברות",
        403 => "אין הרשאה לפעולה זו",
        404 => "המשאב לא נמצא",
        409 => "הבקשה מתנגשת במצב הנוכחי של המשאב",
        _ => "הבקשה נכשלה"
    };
}
