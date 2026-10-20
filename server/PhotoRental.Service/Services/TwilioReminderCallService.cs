namespace PhotoRental.Service.Services;

using System.Net.Http.Headers;
using System.Text;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// שיחת תזכורת אמיתית דרך Twilio: מחייגת לשוכרת ומקריאה את ההודעה (פעמיים) בעברית. בניגוד לשיחת אימות
// הזמינות אין כאן לחיצות ולכן אין צורך ב-Webhook (Twilio:PublicBaseUrl) - רק SID, אסימון ומספר מתקשר.
public class TwilioReminderCallService : IReminderCallService
{
    private readonly HttpClient _http;
    private readonly TwilioOptions _options;

    public bool IsEnabled => true;

    public TwilioReminderCallService(HttpClient http, TwilioOptions options)
    {
        _http = http;
        _options = options;
        _http.BaseAddress = new Uri("https://api.twilio.com/2010-04-01/");
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }

    public async Task<ReminderCallResult> CallAsync(string phone, string message, bool repeat = true, string? statusCallbackUrl = null, string? gatherActionUrl = null)
    {
        var twiml = string.IsNullOrWhiteSpace(gatherActionUrl)
            ? CallTwiml.OneWay(message, repeat)
            : CallTwiml.Gather(message, gatherActionUrl);
        return await CallWithTwimlAsync(phone, twiml, statusCallbackUrl);
    }

    public async Task<ReminderCallResult> CallWithTwimlAsync(string phone, string twiml, string? statusCallbackUrl = null)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return new ReminderCallResult { Error = "אין מספר טלפון" };

        var form = new Dictionary<string, string>
        {
            ["To"] = ToE164IsraeliNumber(phone),
            ["From"] = _options.FromPhoneNumber,
            ["Twiml"] = twiml
        };
        if (!string.IsNullOrWhiteSpace(statusCallbackUrl))
        {
            form["StatusCallback"] = statusCallbackUrl;
            form["StatusCallbackMethod"] = "POST";
        }

        try
        {
            var response = await _http.PostAsync($"Accounts/{_options.AccountSid}/Calls.json", new FormUrlEncodedContent(form));
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return new ReminderCallResult { Error = $"Twilio {(int)response.StatusCode}: {body}" };

            string? sid = null;
            try { sid = System.Text.Json.Nodes.JsonNode.Parse(body)?["sid"]?.GetValue<string>(); } catch { }
            return new ReminderCallResult { Placed = true, Sid = sid };
        }
        catch (Exception ex)
        {
            return new ReminderCallResult { Error = ex.Message };
        }
    }

    // ממיר מספר ישראלי בכתיב מקומי (050-1234567) לתסדיר E.164
    private static string ToE164IsraeliNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("972"))
            return "+" + digits;
        if (digits.StartsWith('0'))
            return "+972" + digits[1..];
        return "+" + digits;
    }
}
