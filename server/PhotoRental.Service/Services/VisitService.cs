using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.Service.Services;

// ספירת גולשים: ביקור אחד לכל לשונית חדשה, בלי שום מידע אישי. בוטים לא נספרים
public class VisitService : IVisitService
{
    private static readonly string[] BotMarkers = { "bot", "crawler", "spider", "facebookexternalhit", "whatsapp" };
    private readonly IVisitRepository _visits;

    public VisitService(IVisitRepository visits)
    {
        _visits = visits;
    }

    public async Task RecordAsync(bool isNewVisitor, string? userAgent)
    {
        var ua = (userAgent ?? string.Empty).ToLowerInvariant();
        if (BotMarkers.Any(ua.Contains))
            return;
        try { await _visits.RecordAsync(IsraelTime.Now.ToString("yyyy-MM-dd"), isNewVisitor); } catch { }
    }

    public Task<IReadOnlyList<VisitDayDto>> GetAllAsync() => _visits.GetAllAsync();
}
