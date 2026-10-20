using System.Security.Cryptography;
using System.Text;
using PhotoRental.Core.Options;

namespace PhotoRental.API.Controllers;

// אימות שבקשת Webhook אכן הגיעה משרתי Twilio (HMAC-SHA1 על הכתובת הפומבית + פרמטרי הטופס ממוינים, לפי התיעוד של Twilio)
public static class TwilioSignature
{
    public static bool IsValid(HttpRequest request, IFormCollection form, TwilioOptions options)
    {
        if (!request.Headers.TryGetValue("X-Twilio-Signature", out var signatureHeader))
            return false;

        var url = $"{options.PublicBaseUrl.TrimEnd('/')}{request.Path}{request.QueryString}";
        var sb = new StringBuilder(url);
        foreach (var key in form.Keys.OrderBy(k => k, StringComparer.Ordinal))
            sb.Append(key).Append(form[key]);

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(options.AuthToken));
        var computed = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signatureHeader.ToString()));
    }
}
