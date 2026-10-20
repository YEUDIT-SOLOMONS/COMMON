namespace PhotoRental.Service.Services;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// שליחת אימיילים אמיתיים דרך Resend (https://resend.com/docs/api-reference/emails/send-email).
// נרשם ב-Program.cs רק כאשר הוגדר Resend:ApiKey ב-appsettings.json (אחרת נעשה שימוש ב-NullEmailService).
//
// חשוב: כתובת השולח (Resend:FromEmail) חייבת להיות בדומיין שאומת בפועל בחשבון ה-Resend שלכם
// (Resend -> Domains) - שליחה מדומיין שלא אומת תיכשל. לבדיקות בסביבת פיתוח בלבד ניתן להשתמש
// בכתובת ברירת המחדל של Resend ("onboarding@resend.dev"), שמוגבלת לשליחה רק לכתובת המייל
// הרשומה בחשבון ה-Resend עצמו.
public class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly ResendOptions _options;
    private readonly ILogger<ResendEmailService> _logger;

    public bool IsEnabled => true;

    public ResendEmailService(HttpClient http, ResendOptions options, ILogger<ResendEmailService> logger)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://api.resend.com/");
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        _options = options;
        _logger = logger;
    }

    public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, string? replyTo) =>
        SendAsync(toEmail, toName, subject, htmlBody, attachments, replyTo, null);

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, IReadOnlyList<EmailAttachment>? attachments, string? replyTo, IReadOnlyDictionary<string, string>? headers)
    {
        var replyToAddress = !string.IsNullOrWhiteSpace(replyTo) ? replyTo : _options.ReplyTo;
        var request = new ResendSendRequest
        {
            From = $"{_options.FromName} <{_options.FromEmail}>",
            ReplyTo = string.IsNullOrWhiteSpace(replyToAddress) ? null : new[] { replyToAddress },
            To = new[] { toEmail },
            Subject = subject,
            Html = htmlBody,
            Attachments = attachments is { Count: > 0 }
                ? attachments.Select(a => new ResendAttachment { Filename = a.FileName, Content = a.ContentBase64 }).ToArray()
                : null,
            Headers = headers is { Count: > 0 } ? headers : null
        };

        try
        {
            var response = await _http.PostAsJsonAsync("emails", request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                // כשל בשליחת אימייל לא אמור להפיל פעולה עסקית חשובה יותר (למשל אישור תשלום) -
                // נרשם ליומן בלבד, בדומה לעיקרון "לא לחסום" שכבר קיים ב-CategorySeeder.
                _logger.LogWarning(
                    "שליחת אימייל דרך Resend נכשלה (קוד {StatusCode}) עבור {ToEmail}: {ErrorBody}",
                    (int)response.StatusCode, toEmail, errorBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "החיבור לשרתי Resend נכשל בעת ניסיון לשלוח אימייל אל {ToEmail}", toEmail);
        }
    }

    private class ResendSendRequest
    {
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("reply_to")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? ReplyTo { get; set; }

        [JsonPropertyName("to")]
        public string[] To { get; set; } = Array.Empty<string>();

        [JsonPropertyName("subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("html")]
        public string Html { get; set; } = string.Empty;

        [JsonPropertyName("headers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyDictionary<string, string>? Headers { get; set; }

        [JsonPropertyName("attachments")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResendAttachment[]? Attachments { get; set; }
    }

    private class ResendAttachment
    {
        [JsonPropertyName("filename")]
        public string Filename { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
