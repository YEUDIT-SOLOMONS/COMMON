namespace PhotoRental.Service.Services;

using System.Collections.Concurrent;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;

// שירות המדמה שיחת טלפון אוטומטית (IVR) אל המשכיר לצורך אימות זמינות הפריט בזמן אמת:
// "לחצו 1 אם הפריט פנוי, לחצו 2 אם הפריט תפוס". זהו מימוש הדמיה בלבד - השיחה "מתנהלת" בזיכרון
// השרת (Sessions למטה) ואינה מבצעת חיוג טלפוני אמיתי. הלקוח (RentModal בצד הריאקט) מציג את מסך
// "מתקשרים למשכיר..." ומאפשר להדגים את שתי האפשרויות (1/2) ישירות מהממשק, וכן נופל אוטומטית ל"אין
// מענה" (NoAnswer) אם לא הגיע מענה תוך RingTimeout.
//
// לחיבור ספק טלפוניה אמיתי בעתיד (כגון Twilio): יש לממש IAvailabilityCallService מחדש כך ש-
// StartCallAsync יבצע קריאת API לחיוג יוצא אל מספר הטלפון של המשכיר עם תפריט IVR (Twilio TwiML),
// ו-Webhook נפרד (הדורש כתובת URL פומבית שהשרת חושף כלפי חוץ, למשל דרך ngrok בסביבת פיתוח) יקבל
// את הספרה שלחץ המשכיר בפועל ויעדכן את הסשן - ה-Controller וה-Frontend לא יזדקקו לשינוי.
public class SimulatedAvailabilityCallService : IAvailabilityCallService
{
    // זמן "צלצול" מדומה מרבי לפני שהמערכת מוותרת ומחזירה "אין מענה", אם המשכיר לא הגיב באופן
    // ידני (RespondAsync) קודם לכן דרך לוח הדימוי בממשק.
    private static readonly TimeSpan RingTimeout = TimeSpan.FromSeconds(25);

    // מילון משותף (Static) לכל מופעי השירות - כך שהמצב נשמר גם אם השירות עצמו נרשם כ-Scoped ב-DI
    // (מופע חדש לכל בקשת HTTP), מבלי לגרום לבעיית "Captive Dependency" מול Repositories שהם Scoped.
    private static readonly ConcurrentDictionary<string, CallSession> Sessions = new();

    private readonly IItemRepository _itemRepository;
    private readonly IUserRepository _userRepository;

    public SimulatedAvailabilityCallService(IItemRepository itemRepository, IUserRepository userRepository)
    {
        _itemRepository = itemRepository;
        _userRepository = userRepository;
    }

    public async Task<AvailabilityCallStatusDto> StartCallAsync(AvailabilityCallStartDto request)
    {
        var itemId = request.ItemId;
        if (string.IsNullOrWhiteSpace(itemId))
            throw new ArgumentException("יש לציין מזהה פריט");

        var item = await _itemRepository.GetByIdAsync(itemId)
            ?? throw new KeyNotFoundException("הפריט לא נמצא");
        if (item.DeletedAtUtc is not null) throw new KeyNotFoundException("הפריט לא נמצא");

        var owner = !string.IsNullOrEmpty(item.OwnerId)
            ? await _userRepository.GetByIdAsync(item.OwnerId)
            : null;

        var session = new CallSession
        {
            CallId = Guid.NewGuid().ToString("N"),
            ItemId = itemId,
            OwnerPhone = owner?.Phone ?? string.Empty,
            Status = AvailabilityCallStatus.Ringing,
            StartedAtUtc = DateTime.UtcNow
        };

        Sessions[session.CallId] = session;
        return ToDto(session);
    }

    public Task<AvailabilityCallStatusDto> GetStatusAsync(string callId)
    {
        var session = GetSessionOrThrow(callId);

        // אם עבר זמן הצלצול המרבי ואף אחד לא הגיב ידנית - מדמים "אין מענה" (כמו שיחה אמיתית שלא נענתה)
        if (session.Status == AvailabilityCallStatus.Ringing && DateTime.UtcNow - session.StartedAtUtc > RingTimeout)
        {
            session.Status = AvailabilityCallStatus.NoAnswer;
        }

        return Task.FromResult(ToDto(session));
    }

    public Task<AvailabilityCallStatusDto> RespondAsync(string callId, int digit)
    {
        var session = GetSessionOrThrow(callId);

        if (session.Status != AvailabilityCallStatus.Ringing)
        {
            // השיחה כבר הסתיימה (נענתה/אין מענה) - מחזירים את המצב הנוכחי בלי לשנות שוב
            return Task.FromResult(ToDto(session));
        }

        session.Status = digit switch
        {
            1 => AvailabilityCallStatus.ConfirmedAvailable,
            2 => AvailabilityCallStatus.ConfirmedUnavailable,
            _ => throw new ArgumentException("יש ללחוץ 1 (הפריט פנוי) או 2 (הפריט תפוס) בלבד")
        };

        return Task.FromResult(ToDto(session));
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
        IsSimulated = true
    };

    private class CallSession
    {
        public string CallId { get; set; } = string.Empty;
        public string ItemId { get; set; } = string.Empty;
        public string OwnerPhone { get; set; } = string.Empty;
        public AvailabilityCallStatus Status { get; set; }
        public DateTime StartedAtUtc { get; set; }
    }
}
