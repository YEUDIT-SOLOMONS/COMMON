namespace PhotoRental.Service.Services;

using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

// חיוג טלפוני יוצא אמיתי אל המשכיר לצורך אימות זמינות הפריט, דרך Twilio: "לחצו 1 אם הפריט פנוי,
// לחצו 2 אם הפריט תפוס" - בדיוק כמו ב-SimulatedAvailabilityCallService, אך הפעם השיחה מתבצעת
// בפועל אל הטלפון של המשכיר, וה"לחיצה" מגיעה בחזרה מ-Twilio דרך Webhook אמיתי (ולא מהדפדפן).
//
// חשוב מאוד: Twilio חייב להיות מסוגל לקרוא בחזרה לשרת הזה (Webhook) דרך כתובת URL פומבית -
// לא localhost. בסביבת פיתוח יש לחשוף את השרת המקומי החוצה (למשל דרך ngrok) ולהגדיר את הכתובת
// הזמנית הזו תחת Twilio:PublicBaseUrl ב-appsettings.json. בסביבת ייצור זו כתובת הדומיין האמיתי.
//
// לגבי "מספר כשר": Twilio מאפשר להשתמש בכל מספר טלפון אמיתי שאומת מראש כ"Verified Caller ID"
// בחשבון ה-Twilio (ולא רק במספרי Twilio) בתור FromPhoneNumber - כך שניתן להשתמש במספר אישי/עסקי
// קיים (שכבר "כשר"/מוכר בישראל) בתור מזהה המתקשר, במקום מספר Twilio זר שעלול להיחסם ע"י מסנני
// טלפונים "כשרים". תהליך האימות מתבצע בקונסולת Twilio (Phone Numbers > Verified Caller IDs) ולא
// בקוד הזה.
public class TwilioAvailabilityCallService : IAvailabilityCallService
{
    // סלאש בסוף הכתובת ונתיב יחסי בלי סלאש בהתחלה - אחרת .NET מוחק את "/2010-04-01" מהכתובת (שגיאת Twilio 20404)
    private const string TwilioApiBaseUrl = "https://api.twilio.com/2010-04-01/";

    // אותו עיקרון בדיוק כמו SimulatedAvailabilityCallService - מילון משותף (Static) לכל מופעי
    // השירות, כדי שהמצב יישמר גם כש-Twilio קורא בחזרה ל-Webhook דרך בקשת HTTP נפרדת ומאוחרת יותר
    private static readonly ConcurrentDictionary<string, CallSession> Sessions = new();

    // זמן המתנה מרבי לתגובת המשכיר בפועל בשיחה, לפני ש-Twilio עצמו מוותר (מועבר גם ל-<Gather>)
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly IItemRepository _itemRepository;
    private readonly IUserRepository _userRepository;
    private readonly ICallLogRepository _callLogs;
    private readonly TwilioOptions _options;
    private readonly IEmailService _email;
    private readonly ClientOptions _client;
    private readonly IReminderCallService _calls;
    private readonly IAvailabilityRequestRepository _requests;
    private readonly IItemScheduleService _schedule;

    private const string RetryKind = "AvailabilityRetry";
    private const string RetryScheduled = "RetryScheduled";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(12);
    private const string OwnerEmailKind = "AvailabilityOwnerEmail";
    private const string OwnerEmailScheduled = "EmailScheduled";

    public TwilioAvailabilityCallService(
        HttpClient http,
        IItemRepository itemRepository,
        IUserRepository userRepository,
        ICallLogRepository callLogs,
        TwilioOptions options,
        IEmailService email,
        ClientOptions client,
        IReminderCallService calls,
        IAvailabilityRequestRepository requests,
        IItemScheduleService schedule,
        ILoanRepository loans)
    {
        _loans = loans;
        _requests = requests;
        _schedule = schedule;
        _calls = calls;
        _email = email;
        _client = client;
        _callLogs = callLogs;
        _http = http;
        _itemRepository = itemRepository;
        _userRepository = userRepository;
        _options = options;

        _http.BaseAddress = new Uri(TwilioApiBaseUrl);
        var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
    }

    public async Task<AvailabilityCallStatusDto> StartCallAsync(AvailabilityCallStartDto request)
    {
        var itemId = request.ItemId;
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("יש לציין מזהה פריט");

        var item = await _itemRepository.GetByIdAsync(itemId)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");
        if (item.DeletedAtUtc is not null) throw new KeyNotFoundException("הפריט לא נמצא");

        request.Quantity = Math.Clamp(request.Quantity ?? 1, 1, Math.Max(1, item.Quantity));

        // המשכירה כבר אישרה לאחרונה שהפריט פנוי במועד קרוב (עד 5 שעות לכל צד) והוא עדיין לא הוזמן - לא מתקשרים שוב
        if (await HasRecentConfirmationAsync(item, request))
        {
            var reused = new CallSession
            {
                CallId = Guid.NewGuid().ToString("N"),
                ItemId = itemId,
                Status = AvailabilityCallStatus.ConfirmedAvailable,
                StartedAtUtc = DateTime.UtcNow,
                OwnerId = item.OwnerId,
                StartDate = request.StartDate,
                ReturnDate = request.ReturnDate,
                Request = request
            };
            Sessions[reused.CallId] = reused;
            return ToDto(reused);
        }

        var owner = !string.IsNullOrEmpty(item.OwnerId)
            ? await _userRepository.GetByIdAsync(item.OwnerId)
            : null;

        if (string.IsNullOrWhiteSpace(owner?.Phone))
            throw new InvalidOperationException("לא נמצא מספר טלפון עבור בעל הפריט - לא ניתן לבצע שיחת אימות");

        // לא מתקשרים למשכירה בשבת, בחג או בלילה
        var nowLocal = IsraelTime.Now;
        if (!CallWindow.IsAllowed(nowLocal))
            throw new InvalidOperationException(
                $"בשבת ובחגים לא מתבצעות שיחות אימות זמינות. אפשר לנסות שוב {CallWindow.Describe(CallWindow.NextAllowed(nowLocal))}.");
        var record = await EnsureRequestAsync(request, item, owner);

        if (QuietHours.IsQuiet(owner, nowLocal))
        {
            var quiet = new CallSession
            {
                CallId = Guid.NewGuid().ToString("N"),
                ItemId = itemId,
                OwnerPhone = owner.Phone,
                Status = AvailabilityCallStatus.OwnerQuiet,
                StartedAtUtc = DateTime.UtcNow,
                OwnerId = owner.Id,
                StartDate = request.StartDate,
                ReturnDate = request.ReturnDate,
                Request = request,
                RequestId = record.Id
            };
            Sessions[quiet.CallId] = quiet;
            var dto = ToDto(quiet);
            dto.RetryAtText = CallWindow.Describe(QuietHours.NextAllowed(owner, nowLocal));
            dto.OwnerEmailed = await SendOwnerAnswerEmailAsync(record, item, owner, quietHours: true);
            return dto;
        }

        var announcement = item.IsLocation ? BuildLocationAnnouncement(item.Name, request) : BuildAnnouncement(item.Name, request);
        var callId = Guid.NewGuid().ToString("N");
        var interested = string.IsNullOrEmpty(request.RenterId) ? null : await _userRepository.GetByIdAsync(request.RenterId);
        var log = await _callLogs.CreateAsync(new CallLog
        {
            Kind = "Availability",
            ItemId = itemId,
            ItemName = item.Name,
            ToUserId = owner.Id,
            ToName = owner.Name,
            ToPhone = owner.Phone,
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Placed",
            Message = announcement,
            InterestedEmail = interested?.Email
        });
        var session = new CallSession
        {
            CallId = callId,
            ItemId = itemId,
            OwnerPhone = owner.Phone,
            Status = AvailabilityCallStatus.Ringing,
            StartedAtUtc = DateTime.UtcNow,
            LogId = log.Id,
            Announcement = announcement,
            OwnerId = owner.Id,
            StartDate = request.StartDate,
            ReturnDate = request.ReturnDate,
            Request = request,
            RequestId = record.Id
        };
        Sessions[callId] = session;

        var toNumber = ToE164IsraeliNumber(owner.Phone);
        var webhookUrl = $"{_options.PublicBaseUrl.TrimEnd('/')}/api/availabilitycalls/twilio/webhook?callId={callId}";
        var twiml = BuildGatherTwiml(webhookUrl, announcement);

        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _options.FromPhoneNumber,
            ["Twiml"] = twiml,
            ["MachineDetection"] = "Enable",
            ["AsyncAmd"] = "true",
            ["AsyncAmdStatusCallback"] = $"{_options.PublicBaseUrl.TrimEnd('/')}/api/availabilitycalls/twilio/amd?callId={callId}",
            ["AsyncAmdStatusCallbackMethod"] = "POST"
        };
        if (!string.IsNullOrEmpty(log.Id))
        {
            form["StatusCallback"] = $"{_options.PublicBaseUrl.TrimEnd('/')}/api/calls/twilio/status?logId={log.Id}";
            form["StatusCallbackMethod"] = "POST";
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync($"Accounts/{_options.AccountSid}/Calls.json", new FormUrlEncodedContent(form));
        }
        catch (Exception ex)
        {
            session.Status = AvailabilityCallStatus.NoAnswer;
            await MarkLogFailedAsync(log, ex.Message);
            throw new InvalidOperationException($"החיוג דרך Twilio נכשל: {ex.Message}", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            session.Status = AvailabilityCallStatus.NoAnswer;
            var errorText = await response.Content.ReadAsStringAsync();
            await MarkLogFailedAsync(log, $"Twilio {(int)response.StatusCode}: {errorText}");
            throw new InvalidOperationException($"החיוג דרך Twilio נכשל (קוד {(int)response.StatusCode}): {errorText}");
        }

        try
        {
            var sid = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())?["sid"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(sid))
            {
                log.TwilioSid = sid;
                log.PlacedAtUtc = DateTime.UtcNow;
                await _callLogs.UpdateAsync(log);
            }
        }
        catch
        {
            // היומן אינו חיוני לשיחה עצמה
        }

        return ToDto(session);
    }

    private async Task MarkLogFailedAsync(CallLog log, string error)
    {
        try
        {
            log.Status = "Failed";
            log.Detail = error;
            await _callLogs.UpdateAsync(log);
        }
        catch
        {
            // היומן אינו חיוני
        }
    }

    // מזהה רשומת היומן של השיחה (לעדכון לחיצת המשכירה) והנוסח להשמעה חוזרת (לחיצה על 9)
    public static string? GetLogId(string callId) => Sessions.TryGetValue(callId, out var s) ? s.LogId : null;

    public static string? GetRepeatTwiml(string callId, string webhookUrl) =>
        Sessions.TryGetValue(callId, out var s) && s.Status == AvailabilityCallStatus.Ringing && !string.IsNullOrEmpty(s.Announcement)
            ? BuildGatherTwiml(webhookUrl, s.Announcement)
            : null;

    public Task<AvailabilityCallStatusDto> GetStatusAsync(string callId)
    {
        var session = GetSessionOrThrow(callId);

        // רשת ביטחון: אם Twilio מסיבה כלשהי לא הצליח לקרוא בחזרה ל-Webhook (למשל בעיית רשת זמנית),
        // לא משאירים את המשתמש תקוע לנצח על "מתקשרים..."
        if (session.Status == AvailabilityCallStatus.Ringing && DateTime.UtcNow - session.StartedAtUtc > RingTimeout + TimeSpan.FromSeconds(15))
        {
            session.Status = AvailabilityCallStatus.NoAnswer;
        }

        return Task.FromResult(ToDto(session));
    }

    // מאפשר עדכון ידני של תוצאת השיחה (בעיקר לגיבוי/תמיכה) - הזרימה הרגילה מתעדכנת דרך
    // UpdateFromWebhookAsync כשמגיעה הלחיצה האמיתית מ-Twilio
    public Task<AvailabilityCallStatusDto> RespondAsync(string callId, int digit)
    {
        var session = GetSessionOrThrow(callId);
        ApplyDigit(session, digit);
        return Task.FromResult(ToDto(session));
    }

    // נקרא מה-Controller כשמגיעה קריאת ה-Webhook האמיתית מ-Twilio עם הספרה שהמשכיר לחץ בפועל
    public static void UpdateFromWebhook(string callId, string? digits)
    {
        if (!Sessions.TryGetValue(callId, out var session) || session.Status != AvailabilityCallStatus.Ringing)
            return;

        if (string.IsNullOrEmpty(digits) || !int.TryParse(digits, out var digit))
        {
            session.Status = AvailabilityCallStatus.NoAnswer;
            return;
        }

        ApplyDigit(session, digit);
    }

    private static void ApplyDigit(CallSession session, int digit)
    {
        if (session.Status != AvailabilityCallStatus.Ringing)
            return;

        session.Status = digit switch
        {
            1 => AvailabilityCallStatus.ConfirmedAvailable,
            2 => AvailabilityCallStatus.ConfirmedUnavailable,
            _ => AvailabilityCallStatus.NoAnswer
        };
    }

    // הנוסח שהקריין אומר למשכירה. פרטי ההזמנה (תאריכים וסכום) נאמרים רק אם הגיעו מהלקוח.
    // סטודיו/לוקיישן: יום ושעות
    private static string BuildLocationAnnouncement(string itemName, AvailabilityCallStartDto request)
    {
        var text = new StringBuilder($"שלום, מישהי מתעניינת לשכור את {itemName} שלך");
        var day = CallMessages.SpokenDate(request.StartDate);
        if (day is not null)
            text.Append($" לתאריך {day}");
        if (!string.IsNullOrEmpty(request.StartTime) && !string.IsNullOrEmpty(request.ReturnTime))
            text.Append($", בין השעות {request.StartTime} ל-{request.ReturnTime}");
        if (request.TotalPrice is > 0)
            text.Append($", סך הכל {CallMessages.SpokenAmount(request.TotalPrice.Value)}");
        text.Append(". זוהי שיחה אוטומטית מקומון לאימות זמינות. ");
        text.Append("אם המקום פנוי בשעות האלה, לחצו 1. אם לא, לחצו 2. לשמיעה חוזרת, לחצו 9.");
        return text.ToString();
    }

    private static string BuildAnnouncement(string itemName, AvailabilityCallStartDto request)
    {
        var units = Math.Max(1, request.Quantity ?? 1);
        var text = new StringBuilder(units > 1
            ? $"שלום, מישהי מתעניינת בשכירת {units} יחידות של {itemName} שלך"
            : $"שלום, מישהי מתעניינת בשכירת ציוד {itemName} שלך");

        var from = CallMessages.SpokenDate(request.StartDate);
        var to = CallMessages.SpokenDate(request.ReturnDate);
        var hasDates = from is not null && to is not null;
        if (hasDates)
            text.Append(from == to ? $" לתאריך {from}" : $" לתאריך {from} עד {to}");

        if (request.TotalPrice is > 0)
            text.Append($", סך הכל {CallMessages.SpokenAmount(request.TotalPrice.Value)}");

        var extras = (request.ExtraItemNames ?? new List<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim()[..Math.Min(n.Trim().Length, 60)])
            .Take(5)
            .ToList();
        if (extras.Count > 0)
            text.Append($", ובהזמנה כלולים גם: {string.Join(", ", extras)}");

        text.Append(". זוהי שיחה אוטומטית מקומון לאימות זמינות ציוד להשכרה. ");
        text.Append(units > 1
            ? (hasDates ? $"אם {units} יחידות פנויות לתאריך הנבחר, לחצו 1. " : $"אם {units} יחידות פנויות, לחצו 1. ")
            : (hasDates ? "אם הפריט פנוי לתאריך הנבחר, לחצו 1. " : "אם הפריט פנוי, לחצו 1. "));
        text.Append("אם הפריט אינו פנוי, לחצו 2. לשמיעה חוזרת, לחצו 9.");
        return text.ToString();
    }

    private static string BuildGatherTwiml(string webhookUrl, string announcement)
    {
        var encodedUrl = System.Security.SecurityElement.Escape(webhookUrl);
        var encodedText = System.Security.SecurityElement.Escape(announcement);
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <Response>
              <Gather numDigits="1" timeout="20" actionOnEmptyResult="true" action="{encodedUrl}" method="POST">
                {CallTwiml.SayTag(encodedText)}
              </Gather>
              {CallTwiml.SayTag("לא התקבלה תגובה. להתראות.")}
            </Response>
            """;
    }

    // ממיר מספר טלפון ישראלי בכתיב מקומי (למשל 050-1234567) לתסדיר E.164 הנדרש ע"י Twilio
    // (972+050-1234567). אם המספר כבר כולל קידומת מדינה - לא נוגעים בו.
    private static string ToE164IsraeliNumber(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("972"))
            return "+" + digits;
        if (digits.StartsWith('0'))
            return "+972" + digits[1..];
        return "+" + digits;
    }

    private static CallSession GetSessionOrThrow(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId) || !Sessions.TryGetValue(callId, out var session))
            throw new KeyNotFoundException("שיחת אימות הזמינות לא נמצאה או שפגה");

        return session;
    }

    private static AvailabilityCallStatusDto ToDto(CallSession session) => new()
    {
        CallId = session.CallId,
        ItemId = session.ItemId,
        Status = session.Status.ToString(),
        // מספר הטלפון של המשכירה לעולם לא נשלח לשוכרת
        OwnerPhone = string.Empty,
        IsSimulated = false
    };

    private class CallSession
    {
        public string CallId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string OwnerPhone { get; set; } = string.Empty;
        public AvailabilityCallStatus Status { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public string? LogId { get; set; }
        public string Announcement { get; set; } = string.Empty;
        public string? OwnerId { get; set; }
        public string? StartDate { get; set; }
        public string? ReturnDate { get; set; }
        public AvailabilityCallStartDto? Request { get; set; }
        public string? RetryLogId { get; set; }
        public string? RetryRenterId { get; set; }
        public string? RetryNotify { get; set; }
        public bool RetryNotified { get; set; }
        public string? RequestId { get; set; }
    }

    private sealed class RetryPayload
    {
        public string RenterId { get; set; } = string.Empty;
        public string Notify { get; set; } = "email";
        public AvailabilityCallStartDto Request { get; set; } = new();
    }

    public async Task<string?> ScheduleRetryAsync(string callId, string renterId, string? notify = null)
    {
        var session = GetSessionOrThrow(callId);
        if (session.Status is not (AvailabilityCallStatus.NoAnswer or AvailabilityCallStatus.OwnerQuiet)
            || session.Request is null || !string.IsNullOrEmpty(session.RetryRenterId))
            return null;
        if (!string.IsNullOrEmpty(session.RetryLogId))
            return session.RetryLogId;
        // הניסיון החוזר כבר נקבע אוטומטית (כשהשיחה הסתיימה בלי תשובה) - רק מעדכנים את אופן העדכון לשוכרת
        if (!string.IsNullOrEmpty(session.RequestId) && await _requests.GetByIdAsync(session.RequestId) is { RetryLogId: { Length: > 0 } existingRetry } existingRecord)
        {
            session.RetryLogId = existingRetry;
            if (notify is "phone" or "email" && existingRecord.Notify != notify)
            {
                existingRecord.Notify = notify;
                await _requests.UpdateAsync(existingRecord);
                var retryLog = await _callLogs.GetByIdAsync(existingRetry);
                if (retryLog is not null && ReadPayload(retryLog) is { } payload)
                {
                    payload.Notify = notify;
                    retryLog.Message = System.Text.Json.JsonSerializer.Serialize(payload);
                    await _callLogs.UpdateAsync(retryLog);
                }
            }
            return existingRetry;
        }

        var owner = string.IsNullOrEmpty(session.OwnerId) ? null : await _userRepository.GetByIdAsync(session.OwnerId);
        var item = await _itemRepository.GetByIdAsync(session.ItemId);
        if (owner is null || item is null)
            return null;

        var log = await _callLogs.CreateAsync(new CallLog
        {
            Kind = RetryKind,
            ItemId = item.Id,
            ItemName = item.Name,
            ToUserId = owner.Id,
            ToName = owner.Name,
            ToPhone = owner.Phone,
            CreatedAtUtc = DateTime.UtcNow,
            Status = RetryScheduled,
            InterestedEmail = (await _userRepository.GetByIdAsync(renterId))?.Email,
            ScheduledForLocal = QuietHours.NextAllowed(owner,
                session.Status == AvailabilityCallStatus.OwnerQuiet ? IsraelTime.Now : IsraelTime.Now + RetryDelay),
            Message = System.Text.Json.JsonSerializer.Serialize(new RetryPayload
            {
                RenterId = renterId,
                Notify = notify == "phone" ? "phone" : "email",
                Request = session.Request
            })
        });
        session.RetryLogId = log.Id;

        var record = string.IsNullOrEmpty(session.RequestId) ? null : await _requests.GetByIdAsync(session.RequestId);
        if (record is not null)
        {
            record.Notify = notify == "phone" ? "phone" : "email";
            record.RetryLogId = log.Id;
            record.RetryAtLocal = log.ScheduledForLocal?.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
            await _requests.UpdateAsync(record);

        }
        return log.Id;
    }

    public async Task<(DateTime? CallAtLocal, bool Emailed)> AdminResendAsync(string callLogId, bool call, bool email)
    {
        var source = await _callLogs.GetByIdAsync(callLogId);
        if (source is null || source.Kind is not ("Availability" or RetryKind) || string.IsNullOrEmpty(source.ItemId))
            throw new InvalidOperationException("השיחה לא נמצאה");

        var requestId = source.Kind == RetryKind ? ReadPayload(source)?.Request.RequestId : null;
        var record = !string.IsNullOrEmpty(requestId)
            ? await _requests.GetByIdAsync(requestId)
            : (await _requests.GetByItemAsync(source.ItemId))
                .Where(r => r.OwnerId == source.ToUserId)
                .OrderBy(r => Math.Abs((r.CreatedAtUtc - source.CreatedAtUtc).TotalMinutes))
                .FirstOrDefault();
        if (record is null)
            throw new InvalidOperationException("לא נמצאה בקשת הזמינות של השיחה הזו");
        if (record.Answer is not null)
            throw new InvalidOperationException(record.Answer == "Available" ? "המשכירה כבר ענתה שהפריט פנוי" : "המשכירה כבר ענתה שהפריט לא פנוי");
        if (record.Canceled)
            throw new InvalidOperationException("השוכרת ביטלה את הבקשה");

        var owner = await _userRepository.GetByIdAsync(record.OwnerId);
        var item = await _itemRepository.GetByIdAsync(record.ItemId);
        if (owner is null || item is null)
            throw new InvalidOperationException("המשכירה או הפריט לא נמצאו");

        DateTime? callAt = null;
        if (call)
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<AvailabilityCallStartDto>(record.RequestJson) ?? new AvailabilityCallStartDto();
            request.RequestId = record.Id;
            var at = QuietHours.NextAllowed(owner, IsraelTime.Now);
            var log = await _callLogs.CreateAsync(new CallLog
            {
                Kind = RetryKind,
                ItemId = item.Id,
                ItemName = item.Name,
                ToUserId = owner.Id,
                ToName = owner.Name,
                ToPhone = owner.Phone,
                CreatedAtUtc = DateTime.UtcNow,
                Status = RetryScheduled,
                Detail = "נשלחה שוב ממסך הניהול",
                InterestedEmail = (await _userRepository.GetByIdAsync(record.RenterId))?.Email,
                ScheduledForLocal = at,
                Message = System.Text.Json.JsonSerializer.Serialize(new RetryPayload
                {
                    RenterId = record.RenterId,
                    Notify = record.Notify,
                    Request = request
                })
            });
            record.RetryLogId = log.Id;
            record.RetryAtLocal = at.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
            await _requests.UpdateAsync(record);
            callAt = at;
        }

        var emailed = false;
        if (email)
        {
            record.OwnerEmailed = false;
            emailed = await SendOwnerAnswerEmailAsync(record, item, owner, quietHours: false);
        }
        return (callAt, emailed);
    }

    public async Task CancelRetryAsync(string retryId, string renterId)
    {
        var log = await _callLogs.GetByIdAsync(retryId);
        if (log is null || log.Kind != RetryKind || log.Status != RetryScheduled || ReadPayload(log)?.RenterId != renterId)
            return;
        log.Status = "Canceled";
        log.Detail = "השוכרת ביטלה את הניסיון החוזר";
        await _callLogs.UpdateAsync(log);

        var requestId = ReadPayload(log)?.Request.RequestId;
        var record = string.IsNullOrEmpty(requestId) ? null : await _requests.GetByIdAsync(requestId);
        if (record is not null && record.Answer is null)
        {
            record.Canceled = true;
            await _requests.UpdateAsync(record);
        }
    }

    private static RetryPayload? ReadPayload(CallLog log)
    {
        try { return string.IsNullOrEmpty(log.Message) ? null : System.Text.Json.JsonSerializer.Deserialize<RetryPayload>(log.Message); }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private readonly ILoanRepository _loans;

    // השיחה הסתיימה (לפי Twilio) ולא התקבלה תשובה: בשיחה הראשונה - ניסיון חוזר בעוד 12 דקות;
    // אם גם הניסיון החוזר לא נענה - מייל עם כפתור תשובה למשכירה 12 דקות אחריו
    public async Task CallEndedWithoutAnswerAsync(string logId)
    {
        var log = await _callLogs.GetByIdAsync(logId);
        if (log is null || log.Kind != "Availability" || string.IsNullOrEmpty(log.ItemId))
            return;

        var requestId = Sessions.Values.FirstOrDefault(s => s.LogId == logId)?.RequestId;
        var record = !string.IsNullOrEmpty(requestId)
            ? await _requests.GetByIdAsync(requestId)
            : (await _requests.GetByItemAsync(log.ItemId))
                .Where(r => r.OwnerId == log.ToUserId && r.Answer is null && !r.Canceled && log.CreatedAtUtc - r.CreatedAtUtc < TimeSpan.FromHours(3)
                            && r.CreatedAtUtc <= log.CreatedAtUtc.AddMinutes(1))
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault();
        if (record is null || record.Answer is not null || record.Canceled || string.IsNullOrEmpty(record.RenterId))
            return;

        var owner = await _userRepository.GetByIdAsync(record.OwnerId);
        var item = await _itemRepository.GetByIdAsync(record.ItemId);
        if (owner is null || item is null)
            return;

        if (string.IsNullOrEmpty(record.RetryLogId))
        {
            var request = System.Text.Json.JsonSerializer.Deserialize<AvailabilityCallStartDto>(record.RequestJson) ?? new AvailabilityCallStartDto();
            request.RequestId = record.Id;
            var retry = await _callLogs.CreateAsync(new CallLog
            {
                Kind = RetryKind,
                ItemId = item.Id,
                ItemName = item.Name,
                ToUserId = owner.Id,
                ToName = owner.Name,
                ToPhone = owner.Phone,
                CreatedAtUtc = DateTime.UtcNow,
                Status = RetryScheduled,
                Detail = "ניסיון חוזר אוטומטי - לא ענתה לשיחה הראשונה",
                InterestedEmail = (await _userRepository.GetByIdAsync(record.RenterId))?.Email,
                ScheduledForLocal = QuietHours.NextAllowed(owner, IsraelTime.Now + RetryDelay),
                Message = System.Text.Json.JsonSerializer.Serialize(new RetryPayload { RenterId = record.RenterId, Notify = record.Notify, Request = request })
            });
            record.RetryLogId = retry.Id;
            record.RetryAtLocal = retry.ScheduledForLocal?.ToString("yyyy-MM-ddTHH:mm", System.Globalization.CultureInfo.InvariantCulture);
            await _requests.UpdateAsync(record);
            foreach (var s in Sessions.Values.Where(s => s.LogId == logId))
                s.RetryLogId = retry.Id;
            return;
        }

        // הניסיון החוזר עוד לא יצא (נקבע כבר מחלון ההזמנה) - זו השיחה הראשונה, אין מה לעשות
        var retryLog = await _callLogs.GetByIdAsync(record.RetryLogId);
        if (retryLog is null || retryLog.Status == RetryScheduled || retryLog.PlacedAtUtc is null || log.CreatedAtUtc < retryLog.PlacedAtUtc.Value.AddSeconds(-30))
            return;

        // גם הניסיון החוזר לא נענה - מייל למשכירה בעוד 12 דקות (פעם אחת)
        if (record.OwnerEmailed || (await _callLogs.GetByStatusAsync(OwnerEmailScheduled)).Any(l => l.Kind == OwnerEmailKind && l.Message == record.Id))
            return;
        await _callLogs.CreateAsync(new CallLog
        {
            Kind = OwnerEmailKind,
            ItemId = item.Id,
            ItemName = item.Name,
            ToUserId = owner.Id,
            ToName = owner.Name,
            ToPhone = owner.Phone,
            CreatedAtUtc = DateTime.UtcNow,
            Status = OwnerEmailScheduled,
            Detail = "מייל לתשובה - לא ענתה לשתי שיחות",
            ScheduledForLocal = IsraelTime.Now + RetryDelay,
            Message = record.Id
        });
    }

    private async Task SendDueOwnerEmailsAsync()
    {
        var now = IsraelTime.Now;
        foreach (var log in (await _callLogs.GetByStatusAsync(OwnerEmailScheduled))
                     .Where(l => l.Kind == OwnerEmailKind && l.ScheduledForLocal is not null && l.ScheduledForLocal <= now))
        {
            var record = string.IsNullOrEmpty(log.Message) ? null : await _requests.GetByIdAsync(log.Message);
            var owner = record is null ? null : await _userRepository.GetByIdAsync(record.OwnerId);
            var item = record is null ? null : await _itemRepository.GetByIdAsync(record.ItemId);
            if (record is null || owner is null || item is null || record.Answer is not null || record.Canceled)
            {
                log.Status = "Canceled";
                log.Detail = "כבר התקבלה תשובה";
                await _callLogs.UpdateAsync(log);
                continue;
            }
            var sent = await SendOwnerAnswerEmailAsync(record, item, owner, quietHours: false);
            log.Status = sent ? "Answered" : "Failed";
            log.Detail = sent ? "נשלח מייל לתשובה למשכירה" : "המייל לא נשלח (אין כתובת או שכבר נשלח)";
            log.PlacedAtUtc = DateTime.UtcNow;
            await _callLogs.UpdateAsync(log);
        }
    }
    private static readonly TimeSpan OrderReminderDelay = TimeSpan.FromMinutes(40);

    // 40 דקות אחרי שהמשכירה אישרה שהפריט פנוי - מייל לשוכרת, רק אם עוד לא הזמינה
    private async Task SendOrderRemindersAsync()
    {
        if (!_email.IsEnabled)
            return;
        var now = DateTime.UtcNow;
        var due = (await _requests.GetAnsweredAvailableAsync())
            .Where(r => !r.Canceled && r.OrderReminderSentAtUtc is null && r.AnsweredAtUtc is { } at
                        && now - at >= OrderReminderDelay && now - at < TimeSpan.FromHours(6) && !string.IsNullOrEmpty(r.RenterId))
            .ToList();
        if (due.Count == 0)
            return;
        var loans = await _loans.GetAllAsync();

        foreach (var record in due)
        {
            record.OrderReminderSentAtUtc = now;
            await _requests.UpdateAsync(record);

            var ordered = loans.Any(l => l.BorrowerId == record.RenterId && l.ItemCopyId == record.ItemId && l.LoanDate >= record.CreatedAtUtc);
            if (ordered)
                continue;
            var renter = await _userRepository.GetByIdAsync(record.RenterId);
            var item = await _itemRepository.GetByIdAsync(record.ItemId);
            if (renter is null || item is null || item.DeletedAtUtc is not null || string.IsNullOrWhiteSpace(renter.Email))
                continue;

            var request = string.IsNullOrEmpty(record.RequestJson)
                ? new AvailabilityCallStartDto { ItemId = record.ItemId, StartDate = record.StartDate, ReturnDate = record.ReturnDate }
                : System.Text.Json.JsonSerializer.Deserialize<AvailabilityCallStartDto>(record.RequestJson) ?? new AvailabilityCallStartDto();
            string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var link = $"{_client.BaseUrl.TrimEnd('/')}/?item={Uri.EscapeDataString(item.Id ?? string.Empty)}&from={Uri.EscapeDataString(record.StartDate ?? string.Empty)}&to={Uri.EscapeDataString(record.ReturnDate ?? string.Empty)}";
            var units = Math.Max(1, request.Quantity ?? 1);
            if (units > 1)
                link += $"&q={units}";
            if (!string.IsNullOrEmpty(request.StartTime) && !string.IsNullOrEmpty(request.ReturnTime))
                link += $"&st={Uri.EscapeDataString(request.StartTime)}&rt={Uri.EscapeDataString(request.ReturnTime)}";
            link += $"&confirmed={Uri.EscapeDataString(record.Id ?? string.Empty)}";
            var dates = record.StartDate == record.ReturnDate
                ? CallMessages.SpokenDate(record.StartDate)
                : $"{CallMessages.SpokenDate(record.StartDate)} עד {CallMessages.SpokenDate(record.ReturnDate)}";
            var name = LoanText.WithUnits(item.Name, units);

            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>שלום {H(NameText.FirstName(renter.Name))},</p>" +
                       $"<p>הצלמת אישרה ש<strong>{H(name)}</strong> פנוי בתאריכים שביקשת ({H(dates)}).</p>" +
                       "<p>רק להזכיר: הפריט עדיין לא שמור עבורך.</p>" +
                       "<p>כל מה שנשאר זה לבחור איך לקבל אותו ולסיים את ההזמנה - זה לוקח דקה.</p>" +
                       $"<p><a href=\"{H(link)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>להמשך ההזמנה</strong></a></p>" +
                       "<p>שאלה? אפשר לכתוב לצלמת דרך כפתור \"יש לי שאלה\" בחלון הפריט.</p>" +
                       "<p>common</p></div>";
            try
            {
                await _email.SendAsync(renter.Email, renter.Name, $"{name} עדיין מחכה לך - common", html);
            }
            catch
            {
                // לא קריטי
            }
        }
    }

    public async Task ProcessRetriesAsync()
    {
        var now = IsraelTime.Now;
        var due = (await _callLogs.GetByStatusAsync(RetryScheduled))
            .Where(l => l.Kind == RetryKind && l.ScheduledForLocal is not null && l.ScheduledForLocal <= now)
            .ToList();

        foreach (var log in due)
        {
            var payload = ReadPayload(log);
            var pending = string.IsNullOrEmpty(payload?.Request.RequestId) ? null : await _requests.GetByIdAsync(payload!.Request.RequestId!);
            if (pending is not null && (pending.Answer is not null || pending.Canceled))
            {
                log.Status = "Canceled";
                log.Detail = "כבר התקבלה תשובה";
                await _callLogs.UpdateAsync(log);
                continue;
            }
            log.Status = "Placed";
            log.PlacedAtUtc = DateTime.UtcNow;
            await _callLogs.UpdateAsync(log);
            if (payload is null)
                continue;

            try
            {
                var started = await StartCallAsync(payload.Request);
                if (Sessions.TryGetValue(started.CallId, out var session))
                {
                    session.RetryRenterId = payload.RenterId;
                    session.RetryNotify = payload.Notify;
                }
            }
            catch (Exception ex)
            {
                log.Detail = ex.Message;
                await _callLogs.UpdateAsync(log);
                await NotifyRenterAsync(payload.RenterId, payload.Notify, payload.Request, AvailabilityCallStatus.NoAnswer, null);
            }
        }

        await NotifyRetryResultsAsync();
        try { await SendOrderRemindersAsync(); } catch { }
        try { await SendDueOwnerEmailsAsync(); } catch { }
    }

    public async Task NotifyRetryResultsAsync()
    {
        foreach (var session in Sessions.Values.Where(s => !string.IsNullOrEmpty(s.RetryRenterId) && !s.RetryNotified).ToList())
        {
            if (session.Status == AvailabilityCallStatus.Ringing && DateTime.UtcNow - session.StartedAtUtc > RingTimeout + TimeSpan.FromMinutes(2))
                session.Status = AvailabilityCallStatus.NoAnswer;
            if (session.Status is AvailabilityCallStatus.Ringing or AvailabilityCallStatus.OwnerQuiet || session.Request is null)
                continue;

            session.RetryNotified = true;
            // תשובה (1/2) כבר טופלה ונשלחה לשוכרת ב-AnswerAsync
            if (session.Status != AvailabilityCallStatus.NoAnswer)
                continue;
            var rec = string.IsNullOrEmpty(session.RequestId) ? null : await _requests.GetByIdAsync(session.RequestId);
            if (rec?.Answer is not null)
                continue;
            await NotifyRenterAsync(session.RetryRenterId!, session.RetryNotify, session.Request, session.Status, session.RequestId);
        }
    }

    // עדכון בטלפון: שיחה קצרה (ובמקרה שהפריט פנוי - גם מייל עם הקישור להמשך). בשעות השקטות של השוכרת - מייל בלבד
    private async Task NotifyRenterAsync(string renterId, string? notify, AvailabilityCallStartDto request, AvailabilityCallStatus status, string? callId)
    {
        var renter = notify == "phone" ? await _userRepository.GetByIdAsync(renterId) : null;
        var item = renter is null ? null : await _itemRepository.GetByIdAsync(request.ItemId);
        var canCall = renter is not null && item is not null && _calls.IsEnabled && !string.IsNullOrWhiteSpace(renter.Phone)
                      && QuietHours.IsAllowed(renter, IsraelTime.Now);
        if (!canCall)
        {
            await SendRetryEmailAsync(renterId, request, status, callId);
            return;
        }

        var name = NameText.FirstName(renter!.Name);
        var message = status switch
        {
            AvailabilityCallStatus.ConfirmedAvailable =>
                $"שלום {name}, זו הודעה מקומון. הצלמת אישרה ש{item!.Name} פנוי לתאריך שביקשת. שלחנו לך מייל עם קישור להמשך ההזמנה. תודה.",
            AvailabilityCallStatus.ConfirmedUnavailable =>
                $"שלום {name}, זו הודעה מקומון. הצלמת עדכנה ש{item!.Name} אינו פנוי לתאריך שביקשת. אפשר לבחור פריט אחר באתר. תודה.",
            _ => $"שלום {name}, זו הודעה מקומון. ניסינו שוב להשיג את הצלמת לגבי {item!.Name}, אך היא לא ענתה. אפשר לנסות שוב מאוחר יותר באתר. תודה."
        };
        try { await _calls.CallAsync(renter.Phone, message); } catch { }
        if (status == AvailabilityCallStatus.ConfirmedAvailable)
            await SendRetryEmailAsync(renterId, request, status, callId);
    }

    private async Task SendRetryEmailAsync(string renterId, AvailabilityCallStartDto request, AvailabilityCallStatus status, string? callId)
    {
        if (!_email.IsEnabled)
            return;
        var renter = await _userRepository.GetByIdAsync(renterId);
        var item = await _itemRepository.GetByIdAsync(request.ItemId);
        if (renter is null || item is null || string.IsNullOrWhiteSpace(renter.Email))
            return;

        string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
        var baseUrl = _client.BaseUrl.TrimEnd('/');
        var dates = request.StartDate == request.ReturnDate
            ? CallMessages.SpokenDate(request.StartDate)
            : $"{CallMessages.SpokenDate(request.StartDate)} עד {CallMessages.SpokenDate(request.ReturnDate)}";
        var itemLink = $"{baseUrl}/?item={Uri.EscapeDataString(item.Id ?? string.Empty)}&from={Uri.EscapeDataString(request.StartDate ?? string.Empty)}&to={Uri.EscapeDataString(request.ReturnDate ?? string.Empty)}";
        var units = Math.Max(1, request.Quantity ?? 1);
        if (units > 1)
            itemLink += $"&q={units}";
        var name = LoanText.WithUnits(item.Name, units);
        if (!string.IsNullOrEmpty(request.StartTime) && !string.IsNullOrEmpty(request.ReturnTime))
            itemLink += $"&st={Uri.EscapeDataString(request.StartTime)}&rt={Uri.EscapeDataString(request.ReturnTime)}";
        const string button = "display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none";

        var (subject, body) = status switch
        {
            AvailabilityCallStatus.ConfirmedAvailable => (
                $"{name} פנוי לתאריך שביקשת! - common",
                $"<p>הצלמת אישרה ש<strong>{H(name)}</strong> פנוי לתאריך שביקשת ({H(dates)}).</p>" +
                "<p>הפריט עדיין לא שמור עבורך - כדאי להשלים את ההזמנה בהקדם.</p>" +
                $"<p><a href=\"{H(itemLink)}&confirmed={Uri.EscapeDataString(callId ?? string.Empty)}\" style=\"{button}\"><strong>להמשך ההזמנה</strong></a></p>"),
            AvailabilityCallStatus.ConfirmedUnavailable => (
                $"{name} אינו פנוי לתאריך שביקשת - common",
                $"<p>הצלמת עדכנה ש<strong>{H(name)}</strong> אינו פנוי לתאריך שביקשת ({H(dates)}).</p>" +
                $"<p><a href=\"{H(baseUrl)}\" style=\"{button}\"><strong>לקטלוג הציוד</strong></a></p>"),
            _ => (
                $"לא הצלחנו לאמת זמינות של {name} - common",
                $"<p>ניסינו שוב להשיג את הצלמת לגבי <strong>{H(name)}</strong> ({H(dates)}), אך היא לא ענתה.</p>" +
                "<p>אפשר לנסות שוב מאוחר יותר, או לבחור פריט אחר.</p>" +
                $"<p><a href=\"{H(itemLink)}\" style=\"{button}\"><strong>לפריט</strong></a></p>")
        };

        var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                   $"<p>שלום {H(NameText.FirstName(renter.Name))},</p>" + body +
                   "<p>common</p></div>";
        try { await _email.SendAsync(renter.Email, renter.Name, subject, html); } catch { }
    }

    private static readonly TimeSpan ConfirmationSlack = TimeSpan.FromHours(5);

    private static (DateTime Start, DateTime End)? WindowOf(string? startDate, string? returnDate, string? startTime, string? returnTime)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        if (!DateTime.TryParseExact(startDate, "yyyy-MM-dd", ci, System.Globalization.DateTimeStyles.None, out var from) ||
            !DateTime.TryParseExact(returnDate, "yyyy-MM-dd", ci, System.Globalization.DateTimeStyles.None, out var to))
            return null;
        var st = TimeSpan.TryParse(startTime, ci, out var s) ? s : TimeSpan.Zero;
        var rt = TimeSpan.TryParse(returnTime, ci, out var r) ? r : new TimeSpan(23, 59, 0);
        return (from.Date + st, to.Date + rt);
    }

    public async Task<bool> IsRecentlyConfirmedAsync(AvailabilityCallStartDto request)
    {
        var item = string.IsNullOrWhiteSpace(request.ItemId) ? null : await _itemRepository.GetByIdAsync(request.ItemId);
        if (item is null || item.DeletedAtUtc is not null)
            return false;
        request.Quantity = Math.Clamp(request.Quantity ?? 1, 1, Math.Max(1, item.Quantity));
        return await HasRecentConfirmationAsync(item, request);
    }

    private async Task<bool> HasRecentConfirmationAsync(Item item, AvailabilityCallStartDto request)
    {
        if (WindowOf(request.StartDate, request.ReturnDate, request.StartTime, request.ReturnTime) is not { } wanted || wanted.Start < IsraelTime.Now)
            return false;
        var records = await _requests.GetByItemAsync(item.Id!);
        AvailabilityCallStartDto? Payload(AvailabilityRequest r)
        {
            try { return string.IsNullOrEmpty(r.RequestJson) ? null : System.Text.Json.JsonSerializer.Deserialize<AvailabilityCallStartDto>(r.RequestJson); }
            catch (System.Text.Json.JsonException) { return null; }
        }

        DateTime? confirmedAt = null;
        foreach (var r in records.Where(r => r.Answer == "Available" && r.AnsweredAtUtc is not null))
        {
            var p = Payload(r);
            if (WindowOf(r.StartDate, r.ReturnDate, p?.StartTime, p?.ReturnTime) is not { } known)
                continue;
            if (wanted.Start >= known.Start - ConfirmationSlack && wanted.End <= known.End + ConfirmationSlack
                && Math.Max(1, request.Quantity ?? 1) <= Math.Max(1, p?.Quantity ?? 1)
                && (confirmedAt is null || r.AnsweredAtUtc > confirmedAt))
                confirmedAt = r.AnsweredAtUtc;
        }
        if (confirmedAt is null)
            return false;

        // תשובת "לא פנוי" מאוחרת יותר על מועד חופף מבטלת את האישור
        foreach (var r in records.Where(r => r.Answer == "Unavailable" && r.AnsweredAtUtc > confirmedAt))
        {
            var p = Payload(r);
            if (WindowOf(r.StartDate, r.ReturnDate, p?.StartTime, p?.ReturnTime) is { } busy && busy.Start < wanted.End && busy.End > wanted.Start)
                return false;
        }

        try
        {
            await _schedule.EnsureFreeAsync(item, wanted.Start, wanted.End, Math.Max(1, request.Quantity ?? 1));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int RequestedUnits(AvailabilityRequest record)
    {
        try
        {
            return string.IsNullOrEmpty(record.RequestJson)
                ? 1
                : Math.Max(1, System.Text.Json.JsonSerializer.Deserialize<AvailabilityCallStartDto>(record.RequestJson)?.Quantity ?? 1);
        }
        catch (System.Text.Json.JsonException)
        {
            return 1;
        }
    }

    private static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task<AvailabilityRequest> EnsureRequestAsync(AvailabilityCallStartDto request, Item item, User owner)
    {
        if (!string.IsNullOrEmpty(request.RequestId) && await _requests.GetByIdAsync(request.RequestId) is { } existing)
            return existing;

        var record = await _requests.CreateAsync(new AvailabilityRequest
        {
            ItemId = item.Id ?? string.Empty,
            OwnerId = owner.Id ?? string.Empty,
            RenterId = request.RenterId ?? string.Empty,
            StartDate = request.StartDate,
            ReturnDate = request.ReturnDate,
            Token = NewToken(),
            CreatedAtUtc = DateTime.UtcNow
        });
        request.RequestId = record.Id;
        record.RequestJson = System.Text.Json.JsonSerializer.Serialize(request);
        await _requests.UpdateAsync(record);
        return record;
    }

    private string DatesText(string? start, string? end) =>
        start == end ? CallMessages.SpokenDate(start) ?? string.Empty : $"{CallMessages.SpokenDate(start)} עד {CallMessages.SpokenDate(end)}";

    // מייל למשכירה עם כפתור תשובה (פנוי / תפוס) - כשלא ענתה לשיחה, או כשהבקשה הגיעה בשעות השקטות שלה
    private async Task<bool> SendOwnerAnswerEmailAsync(AvailabilityRequest record, Item item, User owner, bool quietHours)
    {
        if (!_email.IsEnabled || record.OwnerEmailed || string.IsNullOrWhiteSpace(owner.Email))
            return false;

        string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
        var link = $"{_client.BaseUrl.TrimEnd('/')}/availability-answer/{record.Id}?t={record.Token}";
        var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                   $"<p>שלום {H(NameText.FirstName(owner.Name))},</p>" +
                   $"<p>צלמת מהקהילה מבקשת לשכור את <strong>{H(LoanText.WithUnits(item.Name, RequestedUnits(record)))}</strong> ({H(DatesText(record.StartDate, record.ReturnDate))}).</p>" +
                   (quietHours
                       ? "<p>לא התקשרנו אלייך כי זה בשעות השקטות שלך. אם יש לך רגע - אפשר לענות כאן:</p>"
                       : "<p>ניסינו להתקשר אלייך ולא הצלחנו. אפשר לענות כאן:</p>") +
                   $"<p><a href=\"{H(link)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>האם הפריט פנוי? לתשובה</strong></a></p>" +
                   "<p>common</p></div>";
        try
        {
            await _email.SendAsync(owner.Email, owner.Name, $"האם {item.Name} פנוי? - common", html);
            record.OwnerEmailed = true;
            await _requests.UpdateAsync(record);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task HandlePhoneAnswerAsync(string callId, string? digits)
    {
        if (digits is not ("1" or "2") || !Sessions.TryGetValue(callId, out var session) || string.IsNullOrEmpty(session.RequestId))
            return;
        var record = await _requests.GetByIdAsync(session.RequestId);
        if (record is null)
            return;
        await AnswerAsync(record, digits == "1", "phone", notifyRenter: !string.IsNullOrEmpty(session.RetryRenterId));
    }

    // משיבון / תא קולי: מנתקים ומציגים לשוכרת "לא ענתה" מיד
    public async Task HandleMachineAsync(string callId)
    {
        if (!Sessions.TryGetValue(callId, out var session) || session.Status != AvailabilityCallStatus.Ringing)
            return;
        session.Status = AvailabilityCallStatus.NoAnswer;

        var log = string.IsNullOrEmpty(session.LogId) ? null : await _callLogs.GetByIdAsync(session.LogId);
        if (string.IsNullOrEmpty(log?.TwilioSid))
            return;
        try
        {
            await _http.PostAsync($"Accounts/{_options.AccountSid}/Calls/{log.TwilioSid}.json",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "completed" }));
            log.Detail = "ענה משיבון - השיחה נותקה";
            await _callLogs.UpdateAsync(log);
        }
        catch
        {
            // אם הניתוק נכשל - השיחה תסתיים מעצמה
        }
    }

    private async Task<string> AnswerAsync(AvailabilityRequest record, bool available, string via, bool notifyRenter)
    {
        if (record.Answer is not null)
            return "already";
        if (record.Canceled && via == "email")
            return "canceled";
        if (DateTime.TryParseExact(record.StartDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var start) && start.Date < IsraelTime.Now.Date)
            return "irrelevant";

        var item = await _itemRepository.GetByIdAsync(record.ItemId);
        if (item is null)
            return "irrelevant";

        if (available && via == "email" && DateTime.TryParseExact(record.ReturnDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var end))
        {
            try { await _schedule.EnsureFreeAsync(item, start.Date.AddHours(12), end.Date.AddHours(12), RequestedUnits(record)); }
            catch (InvalidOperationException) { return "booked"; }
        }

        record.Answer = available ? "Available" : "Unavailable";
        record.AnsweredVia = via;
        record.AnsweredAtUtc = DateTime.UtcNow;
        await _requests.UpdateAsync(record);

        // חסימת התאריכים רק לפריט עם עותק אחד - כשיש כמה עותקים, "תפוס" מתייחס רק לכמות שבוקשה
        if (!available && item.Quantity <= 1 && !item.IsLocation && !string.IsNullOrEmpty(record.StartDate) && !string.IsNullOrEmpty(record.ReturnDate))
        {
            try
            {
                await _schedule.AddBlockAsync(item.Id!, record.OwnerId,
                    new ItemBlockCreateDto { From = record.StartDate, To = record.ReturnDate, Note = "סומן תפוס באימות זמינות" });
            }
            catch
            {
                // כבר יש השכרה בחלק מהתאריכים - אין צורך בחסימה
            }
        }

        foreach (var pendingLog in (await _callLogs.GetByStatusAsync(RetryScheduled)).Where(l => l.Kind == RetryKind))
        {
            if (ReadPayload(pendingLog)?.Request.RequestId != record.Id)
                continue;
            pendingLog.Status = "Canceled";
            pendingLog.Detail = "התקבלה תשובה";
            await _callLogs.UpdateAsync(pendingLog);
        }

        if ((notifyRenter || via == "email") && !string.IsNullOrEmpty(record.RenterId))
        {
            var request = string.IsNullOrEmpty(record.RequestJson)
                ? new AvailabilityCallStartDto { ItemId = record.ItemId, StartDate = record.StartDate, ReturnDate = record.ReturnDate }
                : System.Text.Json.JsonSerializer.Deserialize<AvailabilityCallStartDto>(record.RequestJson)!;
            await NotifyRenterAsync(record.RenterId, record.Notify, request,
                available ? AvailabilityCallStatus.ConfirmedAvailable : AvailabilityCallStatus.ConfirmedUnavailable, record.Id);
        }
        return "ok";
    }

    private async Task<AvailabilityAnswerViewDto> AnswerViewAsync(AvailabilityRequest record, string? outcome)
    {
        var item = await _itemRepository.GetByIdAsync(record.ItemId);
        return new AvailabilityAnswerViewDto
        {
            ItemName = item is null ? string.Empty : LoanText.WithUnits(item.Name, RequestedUnits(record)),
            StartDate = record.StartDate,
            ReturnDate = record.ReturnDate,
            Answer = record.Answer,
            Canceled = record.Canceled,
            Outcome = outcome
        };
    }

    private async Task<AvailabilityRequest?> ValidRecordAsync(string requestId, string? token)
    {
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(token))
            return null;
        var record = await _requests.GetByIdAsync(requestId);
        return record is not null && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(record.Token), Encoding.UTF8.GetBytes(token))
            ? record
            : null;
    }

    public async Task<AvailabilityAnswerViewDto?> GetAnswerViewAsync(string requestId, string? token)
    {
        var record = await ValidRecordAsync(requestId, token);
        return record is null ? null : await AnswerViewAsync(record, null);
    }

    public async Task<AvailabilityAnswerViewDto?> AnswerByEmailAsync(string requestId, string? token, bool available)
    {
        var record = await ValidRecordAsync(requestId, token);
        if (record is null)
            return null;
        var outcome = await AnswerAsync(record, available, "email", notifyRenter: true);
        return await AnswerViewAsync(record, outcome);
    }

    public async Task<AvailabilityRequestStatusDto?> GetRequestForRenterAsync(string requestId, string renterId)
    {
        var record = await _requests.GetByIdAsync(requestId);
        if (record is null || record.RenterId != renterId)
            return null;
        return new AvailabilityRequestStatusDto
        {
            Status = record.Answer switch
            {
                "Available" => nameof(AvailabilityCallStatus.ConfirmedAvailable),
                "Unavailable" => nameof(AvailabilityCallStatus.ConfirmedUnavailable),
                _ => nameof(AvailabilityCallStatus.NoAnswer)
            },
            ItemId = record.ItemId,
            StartDate = record.StartDate,
            ReturnDate = record.ReturnDate
        };
    }

    public async Task<IReadOnlyList<RenterWaitingRequestDto>> GetWaitingForRenterAsync(string renterId)
    {
        var now = DateTime.UtcNow;
        var list = new List<RenterWaitingRequestDto>();
        foreach (var r in (await _requests.GetByRenterAsync(renterId)).Where(r => now - r.CreatedAtUtc < TimeSpan.FromDays(3)))
        {
            var followUp = !string.IsNullOrEmpty(r.RetryLogId) || r.OwnerEmailed;
            var waiting = r.Answer is null && !r.Canceled && followUp;
            var recentAnswer = r.Answer is not null && r.AnsweredAtUtc is { } at && now - at < TimeSpan.FromDays(2)
                               && (r.AnsweredVia == "email" || !string.IsNullOrEmpty(r.RetryLogId));
            if (!waiting && !recentAnswer)
                continue;
            var item = await _itemRepository.GetByIdAsync(r.ItemId);
            AvailabilityCallStartDto? original = null;
            try { original = string.IsNullOrEmpty(r.RequestJson) ? null : System.Text.Json.JsonSerializer.Deserialize<AvailabilityCallStartDto>(r.RequestJson); }
            catch (System.Text.Json.JsonException) { }
            list.Add(new RenterWaitingRequestDto
            {
                RequestId = r.Id ?? string.Empty,
                ItemId = r.ItemId,
                ItemName = item?.Name ?? string.Empty,
                StartDate = r.StartDate,
                ReturnDate = r.ReturnDate,
                StartTime = original?.StartTime,
                ReturnTime = original?.ReturnTime,
                Quantity = Math.Max(1, original?.Quantity ?? 1),
                CreatedAtUtc = r.CreatedAtUtc,
                OwnerEmailed = r.OwnerEmailed,
                RetryAtLocal = r.RetryAtLocal,
                Notify = r.Notify,
                Answer = r.Answer,
                AnsweredAtUtc = r.AnsweredAtUtc
            });
        }
        return list.OrderByDescending(x => x.CreatedAtUtc).ToList();
    }

    public async Task CancelRequestAsync(string requestId, string renterId)
    {
        var record = await _requests.GetByIdAsync(requestId);
        if (record is null || record.RenterId != renterId || record.Answer is not null)
            return;
        record.Canceled = true;
        await _requests.UpdateAsync(record);
        if (!string.IsNullOrEmpty(record.RetryLogId) && await _callLogs.GetByIdAsync(record.RetryLogId) is { } log && log.Status == RetryScheduled)
        {
            log.Status = "Canceled";
            log.Detail = "השוכרת ביטלה את הבקשה";
            await _callLogs.UpdateAsync(log);
        }
    }

    public static void MarkEndedWithoutAnswer(string logId)
    {
        foreach (var s in Sessions.Values)
        {
            if (s.LogId == logId && s.Status == AvailabilityCallStatus.Ringing)
                s.Status = AvailabilityCallStatus.NoAnswer;
        }
    }

    // המשכירה לחצה 2 (תפוס): התאריכים שנשאלו, כדי לחסום אותם בלוח הפריט
    public static (string ItemId, string OwnerId, string From, string To)? BusyRange(string callId) =>
        Sessions.TryGetValue(callId, out var s) && s.Status == AvailabilityCallStatus.ConfirmedUnavailable
            && !string.IsNullOrEmpty(s.OwnerId) && !string.IsNullOrEmpty(s.StartDate) && !string.IsNullOrEmpty(s.ReturnDate)
            ? (s.ItemId, s.OwnerId!, s.StartDate!, s.ReturnDate!)
            : null;
}
