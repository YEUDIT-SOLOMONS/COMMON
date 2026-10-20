namespace PhotoRental.Service.Services;

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// סליקת אשראי ב-SUMIT לפי ה-REST API שלהם (https://app.sumit.co.il/developers/api/):
//  - POST billing/payments/beginredirect/ : יוצר דף תשלום מאובטח עם סכום מדויק ומחזיר Data.RedirectURL
//    (חשבונית/קבלה מופקת אוטומטית אחרי התשלום, וההפניה חוזרת אלינו עם og-paymentid ו-og-externalidentifier)
//  - POST billing/payments/get/ : מאמת פרטי תשלום לפי PaymentID (ValidPayment, Amount, ExternalIdentifier)
// ההזדהות: Credentials { CompanyID, APIKey } בגוף כל בקשה. המפתח הפרטי נשמר ב-Secret Manager בלבד.
public class SummitCardPaymentService : ICardPaymentService
{
    private readonly HttpClient _http;
    private readonly SummitOptions _options;

    public SummitCardPaymentService(HttpClient http, SummitOptions options)
    {
        _http = http;
        _options = options;
        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    }

    public bool IsEnabled => true;

    private JsonObject Credentials() => new()
    {
        ["CompanyID"] = _options.CompanyId,
        ["APIKey"] = _options.ApiKey
    };

    public async Task<string> BeginPaymentAsync(CardPaymentRequest request)
    {
        if (request.Amount <= 0)
            throw new ArgumentException("סכום התשלום חייב להיות גדול מאפס");

        var amount = Math.Round(request.Amount, 2);
        var body = new JsonObject
        {
            ["Credentials"] = Credentials(),
            ["Customer"] = new JsonObject
            {
                ["Name"] = string.IsNullOrWhiteSpace(request.CustomerName) ? "לקוחה" : request.CustomerName,
                ["EmailAddress"] = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email,
                ["Phone"] = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone,
                ["ExternalIdentifier"] = request.CustomerExternalId,
                ["SearchMode"] = 2 // חיפוש לקוחה קיימת לפי המזהה החיצוני - תשלומים חוזרים נכנסים לאותו כרטיס
            },
            ["Items"] = new JsonArray
            {
                new JsonObject
                {
                    ["Item"] = new JsonObject { ["Name"] = request.Description },
                    ["Quantity"] = 1,
                    ["UnitPrice"] = amount,
                    ["Description"] = request.Description
                }
            },
            ["VATIncluded"] = true, // הסכום הסופי כבר סופי - לא מוסיפים עליו מע"מ
            ["RedirectURL"] = request.RedirectUrl,
            ["CancelRedirectURL"] = request.CancelUrl,
            ["ExternalIdentifier"] = request.ExternalIdentifier,
            ["MaximumPayments"] = 1,
            ["ExpirationHours"] = 72,
            ["Language"] = 0, // עברית
            ["UpdateOrganizationOnSuccess"] = true,
            ["UpdateCustomerOnSuccess"] = true,
            ["PreventSavingPaymentMethod"] = !request.SavePaymentMethod,
            ["DocumentDescription"] = request.Description
        };

        if (_options.DocumentType is int documentType)
            body["DocumentType"] = documentType;

        var root = await PostAsync("billing/payments/beginredirect/", body, "יצירת דף התשלום");
        var url = root["Data"]?["RedirectURL"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("סאמיט לא החזירה כתובת לדף התשלום");
        return url;
    }

    public async Task<CardChargeResult> ChargeSavedCardAsync(CardChargeRequest request)
    {
        if (request.Amount <= 0)
            return new CardChargeResult { Error = "סכום החיוב חייב להיות גדול מאפס" };

        var body = new JsonObject
        {
            ["Credentials"] = Credentials(),
            ["Customer"] = new JsonObject
            {
                ["Name"] = string.IsNullOrWhiteSpace(request.CustomerName) ? "לקוחה" : request.CustomerName,
                ["EmailAddress"] = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email,
                ["Phone"] = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone,
                ["ExternalIdentifier"] = request.CustomerExternalId,
                ["SearchMode"] = 2 // אותה לקוחה שהכרטיס שלה נשמר בתשלום הראשון (לפי המזהה החיצוני)
            },
            // בלי PaymentMethod - החיוב נעשה באמצעי התשלום השמור של הלקוחה
            ["Items"] = new JsonArray
            {
                new JsonObject
                {
                    ["Item"] = new JsonObject { ["Name"] = request.Description },
                    ["Quantity"] = 1,
                    ["UnitPrice"] = Math.Round(request.Amount, 2),
                    ["Description"] = request.Description
                }
            },
            ["VATIncluded"] = true,
            ["SendDocumentByEmail"] = true,
            ["UpdateCustomerByEmail"] = true,
            ["DocumentDescription"] = request.Description
        };
        if (_options.DocumentType is int documentType)
            body["DocumentType"] = documentType;

        try
        {
            var root = await PostAsync("billing/payments/charge/", body, "חיוב הכרטיס השמור");
            var payment = root["Data"]?["Payment"];
            var valid = payment?["ValidPayment"]?.GetValue<bool>() ?? false;
            if (!valid)
                return new CardChargeResult { Error = payment?["StatusDescription"]?.GetValue<string>() ?? "החיוב לא אושר" };

            return new CardChargeResult { Success = true, PaymentId = payment?["ID"]?.GetValue<long>() };
        }
        catch (InvalidOperationException ex)
        {
            return new CardChargeResult { Error = ex.Message };
        }
    }

    public async Task<CardPaymentInfo?> GetPaymentAsync(long paymentId)
    {
        var body = new JsonObject
        {
            ["Credentials"] = Credentials(),
            ["PaymentID"] = paymentId
        };

        JsonNode root;
        try
        {
            root = await PostAsync("billing/payments/get/", body, "אימות התשלום");
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        var payment = root["Data"]?["Payment"];
        if (payment is null)
            return null;

        return new CardPaymentInfo
        {
            PaymentId = paymentId,
            Valid = payment["ValidPayment"]?.GetValue<bool>() ?? false,
            Amount = (decimal)(payment["Amount"]?.GetValue<double>() ?? 0),
            ExternalIdentifier = payment["ExternalIdentifier"]?.GetValue<string>(),
            Status = payment["Status"]?.GetValue<string>()
        };
    }

    // שולח בקשה ומחזיר את גוף התשובה; זורק InvalidOperationException אם הבקשה נכשלה או שסאמיט דיווחה על שגיאה
    private async Task<JsonNode> PostAsync(string path, JsonObject body, string action)
    {
        HttpResponseMessage response;
        string text;
        try
        {
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            response = await _http.PostAsync(path, content);
            text = await response.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"החיבור לסאמיט נכשל ({action}): {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{action} מול סאמיט נכשלה (קוד {(int)response.StatusCode}): {Truncate(text)}");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch
        {
            throw new InvalidOperationException($"סאמיט החזירה תשובה לא תקינה ({action})");
        }

        // Status: Success (0) / BusinessError (1) / TechnicalError (2) - מספר או טקסט
        var statusNode = root?["Status"];
        var success = statusNode is null
            || (statusNode.GetValueKind() == System.Text.Json.JsonValueKind.Number && statusNode.GetValue<int>() == 0)
            || (statusNode.GetValueKind() == System.Text.Json.JsonValueKind.String
                && statusNode.GetValue<string>().StartsWith("Success", StringComparison.OrdinalIgnoreCase));
        if (!success || root is null)
        {
            var message = root?["UserErrorMessage"]?.GetValue<string>() ?? root?["TechnicalErrorDetails"]?.GetValue<string>() ?? Truncate(text);
            throw new InvalidOperationException($"{action} מול סאמיט נכשלה: {message}");
        }

        return root;
    }

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300];
}
