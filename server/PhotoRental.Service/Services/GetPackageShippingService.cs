namespace PhotoRental.Service.Services;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// חיבור אמיתי לחברת השילוח GetPackage (getpackage.com), לפי התיעוד הרשמי והפומבי שלהם
// (https://docs.getpackage.com, OpenAPI 1.3.0) - חלופה ל-SimulatedShippingService, לשימוש לאחר
// שהוגדר מפתח API אמיתי תחת "GetPackage" ב-appsettings.json (ראו הרשמה ב-Program.cs: כל עוד
// GetPackage:ApiKey ריק, השרת ממשיך להשתמש בשירות המדומה במקום זה).
//
// GetPackage מציעים כמה "מוצרי" משלוח שונים; שניים מהם ממופים כאן לשתי האפשרויות שהאתר מציע:
//   - "Shared Route" (מסלול משותף מתוזמן, זול יותר) = המשלוח ה"רגיל" שלנו
//   - "Express" (מסלול ייעודי, יקר ומהיר יותר) = המשלוח ה"אקספרס" שלנו
// לכל אחד מהם זרימה שונה מעט:
//   - "Shared Route": הזמנת מחיר (POST .../sharedRoute/quote) ופתיחת המשלוח בפועל
//     (POST .../sharedRoute) הן שתי קריאות עצמאיות לחלוטין - אין קשר ביניהן.
//   - "Express": הזמנת מחיר (POST .../express/quote) יוצרת "הצעת מחיר" בחשבון; אישורה בפועל
//     (PUT .../express/quote/accept) תמיד מאשר את ההצעה **האחרונה שנוצרה בחשבון** (כך לפי התיעוד:
//     "Can be requested multiple times, the last one will be used for accept") - לכן, כדי להימנע
//     ממצב שבו הצעת מחיר ישנה מוקדם יותר בתהליך ההשכרה (כשהלקוח רק בדק מחיר) "נתפסת" בטעות בעת
//     האישור בפועל בהמשך, יוצרים כאן הצעת מחיר טרייה ממש רגע לפני האישור (ראו RequestPickupAsync).
//
// כתובת השוכר מגיעה כבר מפוצלת לרכיבים (עיר/רחוב/מספר/קומה - ראו DeliveryAddressDto) מטופס
// ההשכרה בצד הלקוח, ומשמשת ישירות לבניית כתובת מובנית (city/street/number) שה-API מצפה לה -
// מדויק בהרבה מניסיון לפצל מחרוזת כתובת חופשית אחת בשרת (שהתברר לא אמין מול כתובות אמיתיות
// בעברית ללא פסיק עקבי בין רחוב לעיר). כתובת המשכיר (מיקום הפריט), לעומת זאת, עדיין שמורה
// כמחרוזת חופשית אחת ב-Item.Address - עבורה נעזרים בשדה addressLine1 (לא מתועד בסכימת ה-OpenAPI
// שנבדקה, אך מאומת בפועל מול ה-API האמיתי כאלטרנטיבה חוקית ל-city/street/number) - ראו BuildPoint.
public class GetPackageShippingService : IShippingService
{
    private const string SharedRouteQuotePath = "/v1/deliveries/sharedRoute/quote";
    private const string SharedRouteCreatePath = "/v1/deliveries/sharedRoute";
    private const string ExpressQuotePath = "/v1/deliveries/express/quote";
    private const string ExpressAcceptPath = "/v1/deliveries/express/quote/accept";

    // גודל חבילה קבוע (ENVELOPE/SMALL/MEDIUM/LARGE) - ציוד צילום (מצלמות/עדשות/חצובות) מניח
    // גודל "בינוני" כברירת מחדל סבירה; ניתן בעתיד להפוך זאת לשדה מוגדר לכל פריט אם יידרש דיוק רב יותר
    private const string DefaultPackageSize = "MEDIUM";

    private readonly HttpClient _http;

    public GetPackageShippingService(HttpClient http, GetPackageOptions options)
    {
        _http = http;

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            _http.BaseAddress = new Uri(options.BaseUrl);
        }

        // לפי התיעוד: "System expects the API key in form of APIKEY {apikey} in the
        // Authorization header" - לא Bearer הרגיל
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("APIKEY", options.ApiKey);
        _http.DefaultRequestHeaders.Add("X-API-Version", "1.4");
    }

    public async Task<ShippingQuoteDto> GetQuoteAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress)
    {
        var pickup = BuildPoint(pickupAddress);
        var dropoff = BuildPoint(dropoffAddress);

        var sharedRouteTask = GetSharedRouteQuoteAsync(pickup, dropoff);
        var expressTask = GetExpressQuoteOrZeroAsync(pickup, dropoff);
        await Task.WhenAll(sharedRouteTask, expressTask);

        return new ShippingQuoteDto
        {
            RegularPrice = await sharedRouteTask,
            ExpressPrice = await expressTask
        };
    }

    private async Task<decimal> GetExpressQuoteOrZeroAsync(PointBody pickup, PointBody dropoff)
    {
        try
        {
            return await GetExpressQuoteAsync(pickup, dropoff);
        }
        catch (InvalidOperationException)
        {
            return 0m;
        }
    }

    public async Task<ShipmentRequestResult> RequestPickupAsync(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress, DateTime scheduledDate, bool isExpress, TimeSpan? mustArriveBy = null)
    {
        // מוודאים לפני ההזמנה שהמשלוח יגיע עד השעה שהשוכרת ביקשה (ולא נוצרת הזמנה שאי אפשר לעמוד בה)
        string? timeRange = null;
        if (mustArriveBy is { } deadline)
        {
            var check = await CheckArrivalAsync(pickupAddress, dropoffAddress, scheduledDate, deadline, isExpress);
            if (!check.Possible)
                throw new InvalidOperationException(check.Message ?? "לא ניתן להבטיח הגעה עד השעה המבוקשת");
            timeRange = check.TimeRange;
        }

        var pickup = BuildPoint(pickupAddress);
        var dropoff = BuildPoint(dropoffAddress);
        var notes = BuildCourierNotes(pickupAddress, dropoffAddress);

        if (isExpress)
        {
            var body = BuildExpressBody(pickup, dropoff);

            // יוצרים הצעת מחיר טרייה ממש עכשיו (ולא מסתמכים על הצעה קודמת מתהליך ההשכרה) - ראו
            // הערה מפורטת בראש הקובץ לגבי הכלל "ההצעה האחרונה היא זו שתאושר"
            var quoteResponse = await PostAsync(ExpressQuotePath, body);
            await ReadResponseAsync<ExpressQuoteResponse>(quoteResponse, "יצירת הצעת מחיר אקספרס");

            // ההערה לשליח (קומה) נשלחת רק באישור בפועל, לא בהצעת המחיר. אם GetPackage דוחים אותה (400) -
            // מנסים שוב בלי ההערה, כדי שהזמנה לא תיכשל בגלל הערה
            var acceptResponse = await PutAsync(ExpressAcceptPath, BuildExpressBody(pickup, dropoff, notes));
            if (notes is not null && acceptResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                acceptResponse = await PutAsync(ExpressAcceptPath, body);
            }
            var accepted = await ReadResponseAsync<ExpressAcceptResponse>(acceptResponse, "אישור משלוח אקספרס");
            var delivery = accepted.Deliveries?.FirstOrDefault();
            return new ShipmentRequestResult
            {
                TrackingRef = delivery?.SenderTrackingUrl ?? $"GETPACKAGE-ROUTE-{accepted.Id}",
                // מזהה המשלוח (delivery) הוא זה שדרכו שולפים מצב חי; אם לא הוחזר - אין מעקב חי
                ProviderId = delivery is null ? null : delivery.Id.ToString()
            };
        }

        if (timeRange is null)
        {
            var window = await TryEvaluateSharedRouteAsync(pickup, dropoff, scheduledDate)
                ?? throw new InvalidOperationException("לא התקבל חלון הגעה מחברת השילוח - נא לנסות שוב בעוד רגע");
            if (window.Date.Date != scheduledDate.Date)
                throw new InvalidOperationException($"אין משלוח זמין ב-{scheduledDate:dd/MM}. המשלוח הקרוב ביותר הוא ב-{window.Date:dd/MM} - נא לבחור תאריך אחר");
            timeRange = window.Range;
        }

        var createPickup = BuildPoint(pickupAddress, withContact: true);
        var createDropoff = BuildPoint(dropoffAddress, withContact: true);
        createDropoff.ValidationMethodType = "PHOTO";

        var createBody = new SharedRouteCreateRequest
        {
            PickUpPoint = createPickup,
            DropOffPoint = createDropoff,
            Package = new PackageBody { Size = DefaultPackageSize },
            Date = scheduledDate.ToString("yyyy-MM-dd"),
            TimeRange = timeRange,
            Notes = notes
        };

        var response = await PostAsync(SharedRouteCreatePath, createBody);
        if (notes is not null && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // GetPackage דחו את ההערה - פותחים את המשלוח בלעדיה במקום להיכשל
            createBody.Notes = null;
            response = await PostAsync(SharedRouteCreatePath, createBody);
        }
        var result = await ReadResponseAsync<SharedRouteCreateResponse>(response, "פתיחת משלוח רגיל");

        // אימות מול מה ש-GetPackage שיבצה בפועל: התאריך והחלון חייבים להתאים לשעה שביקשה השוכרת.
        // (התאריך עלול לזוז ליום עבודה הבא, למשל בשבת/חג) - אם לא מתאים, מבטלים ולא יוצרים הזמנה
        if (mustArriveBy is { } mustBy)
        {
            var requestedDay = scheduledDate.ToString("yyyy-MM-dd");
            var sameDay = result.Date is null || result.Date.StartsWith(requestedDay, StringComparison.Ordinal);
            var end = result.TimeRange?.Split('-').LastOrDefault()?.Trim();
            var inTime = end is null || !TimeSpan.TryParse(end, System.Globalization.CultureInfo.InvariantCulture, out var endAt) || endAt <= mustBy;
            if (!sameDay || !inTime)
            {
                try { await CancelAsync(result.Id.ToString()); } catch { /* ננסה להמשיך - ההודעה למשתמשת חשובה יותר */ }
                var when = (result.Date?.Length >= 10 ? result.Date[..10] : requestedDay) + (string.IsNullOrWhiteSpace(result.TimeRange) ? string.Empty : $" · {result.TimeRange}");
                throw new InvalidOperationException($"חברת השילוח שיבצה את המשלוח ל-{when}, אחרי השעה שביקשת, ולכן ההזמנה לא נוצרה. נא לבחור שעה או תאריך אחר.");
            }
        }

        return new ShipmentRequestResult
        {
            TrackingRef = result.SenderTrackingUrl ?? $"GETPACKAGE-DELIVERY-{result.Id}",
            ProviderId = result.Id.ToString()
        };
    }

    // משלוח אקספרס מגיע בערך 3 שעות מרגע ההזמנה (ראו גם EstimatedArrivalText ב-LoanService)
    private static readonly TimeSpan ExpressLead = TimeSpan.FromHours(3);

    public async Task<ArrivalCheckDto> CheckArrivalAsync(
        ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress, DateTime date, TimeSpan mustArriveBy, bool isExpress)
    {
        var deadlineText = mustArriveBy.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
        var dateText = date.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);

        if (isExpress)
        {
            var minutes = await TryEvaluateExpressMinutesAsync(BuildPoint(pickupAddress), BuildPoint(dropoffAddress));
            var arrival = IsraelTime.Now + (minutes is { } m ? TimeSpan.FromMinutes(m) : ExpressLead);
            var arrivalText = arrival.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var estimate = minutes is null
                ? "משלוח אקספרס מגיע תוך כ-3 שעות"
                : $"זמן הגעה משוער לפי חברת השילוח: בסביבות {arrivalText}";
            return arrival <= date.Date + mustArriveBy
                ? new ArrivalCheckDto { Possible = true, Verified = true, EstimatedArrival = arrivalText, Message = $"{estimate} - לפני {deadlineText}" }
                : new ArrivalCheckDto
                {
                    Possible = false,
                    Verified = true,
                    EstimatedArrival = arrivalText,
                    Message = $"{estimate}, ולכן לא יספיק עד {deadlineText}. אפשר לבחור שעה מאוחרת יותר או איסוף עצמי."
                };
        }

        (DateTime Date, string Range)? window;
        try
        {
            window = await TryEvaluateSharedRouteAsync(BuildPoint(pickupAddress), BuildPoint(dropoffAddress), date);
        }
        catch (RouteUnavailableException ex)
        {
            return new ArrivalCheckDto { Possible = false, Verified = true, Message = ex.Message };
        }

        if (window is null)
            return new ArrivalCheckDto { Possible = true, Verified = false };

        var (windowDate, range) = window.Value;
        var end = range.Split('-').LastOrDefault()?.Trim();
        if (end is null || !TimeSpan.TryParse(end, System.Globalization.CultureInfo.InvariantCulture, out var endAt))
            return new ArrivalCheckDto { Possible = true, Verified = false };

        if (windowDate.Date != date.Date)
        {
            return new ArrivalCheckDto
            {
                Possible = false,
                Verified = true,
                Message = $"אין משלוח זמין ב-{dateText}. המשלוח הקרוב ביותר הוא ב-{windowDate:dd/MM}, בין {range.Replace("-", " ל-")}. אפשר לבחור תאריך אחר או איסוף עצמי."
            };
        }

        return endAt <= mustArriveBy
            ? new ArrivalCheckDto { Possible = true, Verified = true, TimeRange = range, Message = $"המשלוח יגיע בחלון {range} - לפני {deadlineText}" }
            : new ArrivalCheckDto
            {
                Possible = false,
                Verified = true,
                SuggestedTime = endAt.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture),
                Message = $"חלון ההגעה הקרוב ל-{dateText} הוא {range}, ולכן לא ניתן להבטיח הגעה עד {deadlineText}. אפשר לבחור שעת התחלה אחרי {range.Split('-').Last().Trim()}, תאריך אחר או איסוף עצמי."
            };
    }

    // חלון ההגעה שחברת השילוח מציעה למשלוח רגיל בתאריך נתון (serviceTimes/evaluate). null = לא התקבלה תשובה תקינה
    private async Task<(DateTime Date, string Range)?> TryEvaluateSharedRouteAsync(PointBody pickup, PointBody dropoff, DateTime date)
    {
        try
        {
            var response = await PostAsync("/v1/deliveries/serviceTimes/evaluate", new EvaluateRequest
            {
                PickUpPoint = pickup,
                DropOffPoint = dropoff,
                Date = date.ToString("yyyy-MM-dd")
            });
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                if (errorBody.Contains("timetable:unavailable", StringComparison.Ordinal))
                    throw new RouteUnavailableException();
                return null;
            }

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object
                || !root.TryGetProperty("timeRange", out var tr) || tr.ValueKind != System.Text.Json.JsonValueKind.String
                || !root.TryGetProperty("date", out var d) || d.ValueKind != System.Text.Json.JsonValueKind.String
                || !DateTime.TryParse(d.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var windowDate)
                || tr.GetString() is not { } range || !range.Contains('-'))
                return null;

            return (windowDate, range.Trim());
        }
        catch (Exception ex) when (ex is not RouteUnavailableException && ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // משך מסלול האקספרס בדקות לפי חברת השילוח (serviceTimes/evaluate, durationMin). null = אין תשובה תקינה
    private async Task<double?> TryEvaluateExpressMinutesAsync(PointBody pickup, PointBody dropoff)
    {
        try
        {
            var response = await PostAsync("/v1/deliveries/serviceTimes/evaluate", new EvaluateRequest
            {
                PickUpPoint = pickup,
                DropOffPoint = dropoff,
                Date = IsraelTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                ServiceType = "EXPRESS"
            });
            if (!response.IsSuccessStatusCode)
                return null;

            using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("durationMin", out var d)
                && d.ValueKind == System.Text.Json.JsonValueKind.Number
                && d.GetDouble() is var value and > 0 and < 600
                ? value
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private sealed class RouteUnavailableException : InvalidOperationException
    {
        public RouteUnavailableException()
            : base("משלוח רגיל לא זמין כרגע לכתובת או לתאריך האלה. אפשר לבחור תאריך אחר או איסוף עצמי.")
        {
        }
    }

    // ביטול משלוח: DELETE /v1/deliveries/common/deliveries/{id}. "לא נמצא" נחשב הצלחה (כבר בוטל)
    public async Task CancelAsync(string providerId)
    {
        if (!long.TryParse(providerId, out var id))
            return;

        StatusCache.TryRemove(providerId, out _);

        HttpResponseMessage response;
        try
        {
            response = await _http.DeleteAsync($"/v1/deliveries/common/deliveries/{id}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"החיבור לשרתי GetPackage נכשל: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException($"ביטול המשלוח מול GetPackage נכשל (קוד {(int)response.StatusCode}) - ייתכן שהשליח כבר בדרך");
    }

    // מטמון קצר למצב משלוח: כמה משתמשות שצופות באותו משלוח (או רענון חוזר) לא מכפילות קריאות - לכל קריאה
    // עלות במכסה היומית של GetPackage (ברירת מחדל 3000 יחידות ביום)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, ShipmentStatusDto? Dto)> StatusCache = new();
    private static readonly TimeSpan StatusCacheTtl = TimeSpan.FromSeconds(20);

    public async Task<ShipmentStatusDto?> GetStatusAsync(string providerId)
    {
        if (StatusCache.TryGetValue(providerId, out var hit) && DateTime.UtcNow - hit.At < StatusCacheTtl)
            return hit.Dto;

        var fresh = await GetStatusUncachedAsync(providerId);
        StatusCache[providerId] = (DateTime.UtcNow, fresh);
        return fresh;
    }

    // מצב משלוח חי: GET /v1/deliveries/common/deliveries/{id} (סטטוס, שליח, מיקום, תאריך וחלון זמן, קישור מעקב)
    private async Task<ShipmentStatusDto?> GetStatusUncachedAsync(string providerId)
    {
        if (!long.TryParse(providerId, out var id))
            return null;

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"/v1/deliveries/common/deliveries/{id}");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"החיבור לשרתי GetPackage נכשל: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"קבלת מצב משלוח מול GetPackage נכשלה (קוד {(int)response.StatusCode})");

        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        var status = Str("status") ?? string.Empty;
        var routeStatus = Str("routeStatus") ?? string.Empty;
        var (normalized, label) = MapStatus(status, routeStatus, !string.IsNullOrWhiteSpace(Str("courierName")));

        var dto = new ShipmentStatusDto
        {
            Status = normalized,
            StatusLabel = label,
            EtaTimeRange = Str("timeRange"),
            CourierName = Str("courierName"),
            CourierPhone = Str("courierPhoneNumber"),
            TrackingUrl = Str("senderTrackingUrl")
        };

        if (DateTime.TryParse(Str("date"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var date))
            dto.EtaDate = date;

        if (root.TryGetProperty("location", out var loc) && loc.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (loc.TryGetProperty("lat", out var lat) && lat.ValueKind == System.Text.Json.JsonValueKind.Number) dto.Latitude = lat.GetDouble();
            if (loc.TryGetProperty("lng", out var lng) && lng.ValueKind == System.Text.Json.JsonValueKind.Number) dto.Longitude = lng.GetDouble();
            if (loc.TryGetProperty("updatedAt", out var upd) && upd.ValueKind == System.Text.Json.JsonValueKind.String
                && DateTime.TryParse(upd.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var updated))
                dto.LocationUpdatedAt = updated;
        }

        // שקיפות: "מחפשים שליח" נאמר רק כשבאמת מחפשים (המועד קרוב); אחרת מציגים את המועד המתוזמן
        if (normalized == "Pending")
            dto.StatusLabel = PendingLabel(dto.EtaDate, dto.EtaTimeRange, routeStatus);

        // הערכת הגעה חיה: מרחק מהמיקום האחרון של השליח אל היעד הנוכחי (האיסוף, ואחרי האיסוף - המסירה)
        (double Lat, double Lng)? Point(string name)
        {
            if (root.TryGetProperty(name, out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Object
                && p.TryGetProperty("address", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.Object
                && a.TryGetProperty("latitude", out var la) && la.ValueKind == System.Text.Json.JsonValueKind.Number
                && a.TryGetProperty("longitude", out var lo) && lo.ValueKind == System.Text.Json.JsonValueKind.Number)
                return (la.GetDouble(), lo.GetDouble());
            return null;
        }

        if (dto.Latitude is double courierLat && dto.Longitude is double courierLng && normalized is "Assigned" or "OnTheWay" or "PickedUp")
        {
            var pickup = Point("pickUpPoint");
            var dropoff = Point("dropOffPoint");
            var toDropoff = normalized == "PickedUp";
            if ((toDropoff ? dropoff : pickup) is { } target)
            {
                var remaining = RoadKm(courierLat, courierLng, target.Lat, target.Lng);
                dto.LegTarget = toDropoff ? "Dropoff" : "Pickup";
                dto.RemainingKm = Math.Round(remaining, 1);
                dto.EtaMinutes = Math.Max(1, (int)Math.Ceiling(remaining / AverageSpeedKmh * 60));

                if (toDropoff && pickup is { } from)
                {
                    var total = RoadKm(from.Lat, from.Lng, target.Lat, target.Lng);
                    if (root.TryGetProperty("distance", out var d) && d.ValueKind == System.Text.Json.JsonValueKind.Number)
                        total = Math.Max(total, d.GetDouble());
                    if (total > 0.05)
                        dto.Progress = Math.Clamp(1 - remaining / total, 0.03, 0.97);
                }
            }
        }

        // שליח וטלפון נחשפים רק כשיש שליח משובץ ופעיל, ולא אחרי סיום/ביטול
        if (normalized is "Delivered" or "Canceled" or "Failed")
        {
            dto.LegTarget = null;
            dto.RemainingKm = null;
            dto.EtaMinutes = null;
            dto.Progress = null;
            dto.CourierPhone = null;
            dto.Latitude = null;
            dto.Longitude = null;
        }

        return dto;
    }

    // תיאור סטטוס "ממתין": אם האיסוף/המשלוח מתוזמן לעוד יותר מ-4 שעות - לא מחפשים עדיין שליח, ולכן לא כותבים זאת
    private static string PendingLabel(DateTime? date, string? timeRange, string routeStatus)
    {
        if (date is null)
            return routeStatus == "LOCATING_COURIER" ? "מחפשים שליח למשלוח" : "ההזמנה התקבלה. שליח ישובץ סמוך למועד";

        var start = date.Value.Date;
        var first = timeRange?.Split('-')[0].Trim();
        if (first is not null && TimeSpan.TryParse(first, System.Globalization.CultureInfo.InvariantCulture, out var from))
            start += from;

        if (start - IsraelTime.Now <= TimeSpan.FromHours(4))
            return "מחפשים שליח למשלוח";

        var when = date.Value.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture) +
                   (string.IsNullOrWhiteSpace(timeRange) ? string.Empty : $" · {timeRange}");
        return $"ההזמנה התקבלה ומתוזמנת ל-{when}. שליח ישובץ סמוך למועד";
    }

    // מהירות נסיעה ממוצעת בעיר להערכת זמן הגעה, ומקדם הארכה מקו אווירי למרחק כביש
    private const double AverageSpeedKmh = 30;

    private static double RoadKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthKm = 6371;
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * earthKm * Math.Asin(Math.Min(1, Math.Sqrt(h))) * 1.3;
    }

    // סטטוסי GetPackage (ראו OpenAPI: status ו-routeStatus) -> סטטוס מנורמל ותיאור בעברית
    private static (string Status, string Label) MapStatus(string status, string routeStatus, bool hasCourier)
    {
        // מצבי סיום וחבילה שנאספה - נקבעים לפי סטטוס המשלוח עצמו
        switch (status)
        {
            case "COMPLETED": return ("Delivered", "המשלוח נמסר");
            case "CANCELED": return ("Canceled", "המשלוח בוטל");
            case "CANCELED_BY_COURIER_ON_PICKUP": return ("Canceled", "השליח ביטל את האיסוף");
            case "FAILED_DROPOFF": return ("Failed", "המסירה לא הצליחה");
            case "RETURNED": return ("Failed", "החבילה הוחזרה לשולח");
            case "PICKED_UP": return ("PickedUp", "החבילה אצל השליח ובדרך ליעד");
        }

        // לפני האיסוף: מצב המסלול (routeStatus) הוא המדויק ביותר. "שליח שובץ" נאמר רק כשיש שליח בפועל
        if (routeStatus == "ON_MY_WAY" || status == "STARTED") return ("OnTheWay", "השליח בדרך לאיסוף");
        if (routeStatus == "ASSIGNED" || status == "ASSIGNED" || (status == "IN_ROUTE" && hasCourier))
            return ("Assigned", "שליח שובץ למשלוח");
        if (routeStatus is "CANCELED" or "CANCELED_ON_PICKUP") return ("Canceled", "המשלוח בוטל");
        if (routeStatus == "COMPLETED") return ("Delivered", "המשלוח נמסר");

        return routeStatus == "LOCATING_COURIER"
            ? ("Pending", "מחפשים שליח למשלוח")
            : ("Pending", "ההזמנה התקבלה");
    }

    private async Task<decimal> GetSharedRouteQuoteAsync(PointBody pickup, PointBody dropoff)
    {
        var body = new SharedRouteQuoteRequest
        {
            PickUpPoint = pickup,
            DropOffPoint = dropoff,
            Package = new PackageBody { Size = DefaultPackageSize }
        };

        var response = await PostAsync(SharedRouteQuotePath, body);
        var result = await ReadResponseAsync<QuoteRateResponse>(response, "קבלת הצעת מחיר רגילה");
        // totalRate בפועל לפני מע"מ (42.4) ו-taxRate הוא סכום המע"מ (7.6); serviceTimes/evaluate מחזיר את המחיר המלא (50)
        return result.TaxRate > 0 && result.TaxRate < result.TotalRate ? result.TotalRate + result.TaxRate : result.TotalRate;
    }

    private async Task<decimal> GetExpressQuoteAsync(PointBody pickup, PointBody dropoff)
    {
        var body = BuildExpressBody(pickup, dropoff);
        var response = await PostAsync(ExpressQuotePath, body);
        var result = await ReadResponseAsync<ExpressQuoteResponse>(response, "קבלת הצעת מחיר אקספרס");
        return result.TotalRate;
    }

    private static ExpressRouteRequest BuildExpressBody(PointBody pickup, PointBody dropoff, string? notes = null) => new()
    {
        Deliveries = new List<ExpressDeliveryItem>
        {
            new() { PickUpPoint = pickup, DropOffPoint = dropoff, Notes = notes }
        },
        // עבור משלוח בודד (המקרה היחיד שהאתר תומך בו כרגע) - תמיד [0, 0]: איסוף ומסירה של אותה
        // "משלוח יחיד" באינדקס 0 במערך deliveries (ראו תיעוד השדה stopPointsOrder)
        StopPointsOrder = new List<int> { 0, 0 }
    };

    // בונה נקודת איסוף/מסירה עבור GetPackage מתוך כתובת שעשויה להיות מפורטת (עיר/רחוב/מספר -
    // כתובת שוכר מטופס ההשכרה) או חופשית (כתובת משכיר, כמחרוזת יחידה כפי שכבר שמורה ב-Item.Address).
    // מעדיפים תמיד שדות מובנים (city/street/number) כשקיימים - מדויקים משמעותית; רק אם אין כתובת
    // מפורטת בכלל, נופלים לאחור ל-addressLine1 עם כל הכתובת כמחרוזת אחת.
    //
    // הערה חשובה לגבי קומה (Floor): במקור ניסינו להעביר אותה כשדה "instructions" בנקודת
    // האיסוף/מסירה, אבל מול ה-API האמיתי (סביבת הייצור) זה נדחה בהצעת מחיר עם השגיאה המפורשת
    // "Field instructions is not defined by type QuoteStopPointArgs". לא בדקנו (ומכוונים שלא
    // לבדוק) אם נתיבי ההזמנה בפועל (accept/create) כן תומכים בשדה הזה, כי בדיקה כזו הייתה יוצרת
    // משלוח אמיתי בחשבון הייצור החי - לכן, כדי לא להסתכן בשבירת תהליך הזמנה אמיתי על השערה לא
    // מאומתת, קומה אינה מועברת ל-GetPackage כלל בשלב זה (עדיין נשמרת בתצוגה/במייל דרך
    // LoanService.FormatDeliveryAddress). יש לבדוק מול תיעוד GetPackage (או קריאת ניסיון עם
    // "isDummy": true בנתיב ה-Shared Route create, שמיועד בדיוק ל"הרצה יבשה" לבדיקות) מהו השדה
    // הנכון להעברת הערות לשליח, ואז להוסיף אותו כאן.
    private static PointBody BuildPoint(ShippingAddressDto address, bool withContact = false)
    {
        var hasStructuredAddress = !string.IsNullOrWhiteSpace(address.City) || !string.IsNullOrWhiteSpace(address.Street);

        var addressBody = hasStructuredAddress
            ? new AddressBody
            {
                Country = "IL",
                City = TruncateOrNull(address.City),
                Street = TruncateOrNull(address.Street),
                Number = TruncateOrNull(address.HouseNumber)
            }
            : new AddressBody
            {
                Country = "IL",
                // addressLine1 אינו מתועד בסכימת ה-OpenAPI (שם מוגבלים השדות המובנים ל-50 תווים)
                // ולכן אין לו מגבלת אורך ידועה - לא מקצרים אותו, כדי לא לאבד חלק מהכתובת בטעות
                AddressLine1 = string.IsNullOrWhiteSpace(address.FreeText) ? null : address.FreeText!.Trim()
            };

        var point = new PointBody { Address = addressBody };
        if (withContact)
        {
            point.ContactName = string.IsNullOrWhiteSpace(address.ContactName) ? null : TruncateOrNull(address.ContactName);
            point.ContactPhoneNumber = string.IsNullOrWhiteSpace(address.ContactPhone) ? null : address.ContactPhone.Trim();
        }
        return point;
    }

    // הערה לשליח (שדה notes, עד 250 תווים): הקומה בכתובת המסירה ובכתובת האיסוף, אם נמסרה. GetPackage
    // לא מקבלים קומה כחלק מהכתובת עצמה (ראו הערה ב-BuildPoint)
    private static string? BuildCourierNotes(ShippingAddressDto pickupAddress, ShippingAddressDto dropoffAddress)
    {
        var parts = new List<string>();
        var dropoffDetails = string.Join(", ", new[]
        {
            string.IsNullOrWhiteSpace(dropoffAddress.Floor) ? null : $"קומה {dropoffAddress.Floor.Trim()}",
            string.IsNullOrWhiteSpace(dropoffAddress.Apartment) ? null : $"דירה/משפחה {dropoffAddress.Apartment.Trim()}"
        }.Where(x => x is not null));
        var pickupDetails = string.Join(", ", new[]
        {
            string.IsNullOrWhiteSpace(pickupAddress.Floor) ? null : $"קומה {pickupAddress.Floor.Trim()}",
            string.IsNullOrWhiteSpace(pickupAddress.Apartment) ? null : $"דירה/משפחה {pickupAddress.Apartment.Trim()}"
        }.Where(x => x is not null));
        if (dropoffDetails.Length > 0) parts.Add($"כתובת המסירה: {dropoffDetails}");
        if (pickupDetails.Length > 0) parts.Add($"כתובת האיסוף: {pickupDetails}");
        if (!string.IsNullOrWhiteSpace(pickupAddress.Notes)) parts.Add($"הערות לכתובת האיסוף: {pickupAddress.Notes.Trim()}");
        if (!string.IsNullOrWhiteSpace(dropoffAddress.Notes)) parts.Add($"הערות לכתובת המסירה: {dropoffAddress.Notes.Trim()}");
        if (parts.Count == 0) return null;
        var text = string.Join("; ", parts);
        return text.Length <= 250 ? text : text[..250];
    }

    private static string? TruncateOrNull(string? value) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= 50 ? value : value[..50];

    private async Task<HttpResponseMessage> PostAsync<TBody>(string path, TBody body)
    {
        try
        {
            return await _http.PostAsJsonAsync(path, body);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"החיבור לשרתי GetPackage נכשל: {ex.Message}", ex);
        }
    }

    private async Task<HttpResponseMessage> PutAsync<TBody>(string path, TBody body)
    {
        try
        {
            return await _http.PutAsJsonAsync(path, body);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"החיבור לשרתי GetPackage נכשל: {ex.Message}", ex);
        }
    }

    private static async Task<TResult> ReadResponseAsync<TResult>(HttpResponseMessage response, string actionDescription)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            if (errorText.Contains("outOfAllowedDistance", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("משלוח עם שליח אפשרי כרגע רק למרחק קצר (בדרך כלל באותה עיר או באזור). לכתובת הזו אפשר לבחור איסוף עצמי.");
            throw new InvalidOperationException($"{actionDescription} מול GetPackage נכשלה (קוד {(int)response.StatusCode}): {errorText}");
        }

        return await response.Content.ReadFromJsonAsync<TResult>()
            ?? throw new InvalidOperationException($"GetPackage החזיר תשובה ריקה או לא תקינה עבור {actionDescription}");
    }

    private class AddressBody
    {
        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("street")]
        public string? Street { get; set; }

        [JsonPropertyName("number")]
        public string? Number { get; set; }

       
        [JsonPropertyName("addressLine1")]
        public string? AddressLine1 { get; set; }
    }

    private class PointBody
    {
        [JsonPropertyName("address")]
        public AddressBody Address { get; set; } = new();

        [JsonPropertyName("contactName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContactName { get; set; }

        [JsonPropertyName("contactPhoneNumber")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContactPhoneNumber { get; set; }

        [JsonPropertyName("validationMethodType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ValidationMethodType { get; set; }
    }

    private class PackageBody
    {
        [JsonPropertyName("size")]
        public string Size { get; set; } = DefaultPackageSize;
    }

    private class SharedRouteQuoteRequest
    {
        [JsonPropertyName("pickUpPoint")]
        public PointBody PickUpPoint { get; set; } = new();

        [JsonPropertyName("dropOffPoint")]
        public PointBody DropOffPoint { get; set; } = new();

        [JsonPropertyName("package")]
        public PackageBody Package { get; set; } = new();
    }

    private class SharedRouteCreateRequest
    {
        [JsonPropertyName("pickUpPoint")]
        public PointBody PickUpPoint { get; set; } = new();

        [JsonPropertyName("dropOffPoint")]
        public PointBody DropOffPoint { get; set; } = new();

        [JsonPropertyName("package")]
        public PackageBody Package { get; set; } = new();

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        // חלון הגעה "HH:MM-HH:MM" (משלוח רגיל) - נבחר מתוך serviceTimes כך שיסתיים לפני השעה המבוקשת
        [JsonPropertyName("timeRange")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? TimeRange { get; set; }

        [JsonPropertyName("notes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Notes { get; set; }
    }

    private class EvaluateRequest
    {
        [JsonPropertyName("pickUpPoint")]
        public PointBody PickUpPoint { get; set; } = new();

        [JsonPropertyName("dropOffPoint")]
        public PointBody DropOffPoint { get; set; } = new();

        [JsonPropertyName("package")]
        public PackageBody Package { get; set; } = new();

        [JsonPropertyName("date")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("serviceType")]
        public string ServiceType { get; set; } = "SDD";

        [JsonPropertyName("subServiceType")]
        public string SubServiceType { get; set; } = "GENERAL";
    }

    private class SharedRouteCreateResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("senderTrackingUrl")]
        public string? SenderTrackingUrl { get; set; }

        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("timeRange")]
        public string? TimeRange { get; set; }
    }

    private class ExpressDeliveryItem
    {
        [JsonPropertyName("pickUpPoint")]
        public PointBody PickUpPoint { get; set; } = new();

        [JsonPropertyName("dropOffPoint")]
        public PointBody DropOffPoint { get; set; } = new();

        // במסלול האקספרס החבילה אופציונלית (לפי ה-OpenAPI של GetPackage). שליחת size כמחרוזת ("MEDIUM") נדחית
        // שם בשגיאת 400 ("Enum PackageSize cannot represent non-string value: 2") - כנראה כי השרת שלהם ממיר
        // את הערך למספר לפני שהוא מגיע לשכבה הפנימית - ולכן לא שולחים כאן חבילה כלל.
        [JsonPropertyName("package")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PackageBody? Package { get; set; }

        [JsonPropertyName("notes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Notes { get; set; }
    }

    // אותו מבנה גוף בקשה בדיוק משמש הן ליצירת הצעת מחיר (quote) והן לאישורה בפועל (accept) -
    // ראו הערה בראש הקובץ לגבי הכלל "ההצעה האחרונה היא זו שתאושר"
    private class ExpressRouteRequest
    {
        [JsonPropertyName("deliveries")]
        public List<ExpressDeliveryItem> Deliveries { get; set; } = new();

        [JsonPropertyName("stopPointsOrder")]
        public List<int> StopPointsOrder { get; set; } = new();
    }

    private class QuoteRateResponse
    {
        [JsonPropertyName("totalRate")]
        public decimal TotalRate { get; set; }

        [JsonPropertyName("taxRate")]
        public decimal TaxRate { get; set; }
    }

    private class ExpressQuoteResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("totalRate")]
        public decimal TotalRate { get; set; }
    }

    private class ExpressAcceptResponse
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("deliveries")]
        public List<ExpressAcceptDelivery>? Deliveries { get; set; }
    }

    private class ExpressAcceptDelivery
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("senderTrackingUrl")]
        public string? SenderTrackingUrl { get; set; }
    }
}
