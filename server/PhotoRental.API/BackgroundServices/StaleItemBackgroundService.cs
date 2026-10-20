namespace PhotoRental.API.BackgroundServices;

using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

// בדיקת "פריט שלא הושכר 6 שבועות" (ראו StaleItemService): נבדקת לכל היותר פעם בשעה, ורק בין 09:00 ל-20:00
// שעון ישראל (לא שולחים מיילים בלילה). האידמפוטנטיות נשמרת בפריט עצמו (חותמת "נשלח").
// נקראת מהלולאה הפנימית וגם מ-POST /api/internal/run-scheduled (ראו SchedulerController).
public class StaleItemBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SchedulerOptions _scheduler;
    private static DateTime _lastRunUtc = DateTime.MinValue;

    public StaleItemBackgroundService(IServiceScopeFactory scopeFactory, SchedulerOptions scheduler)
    {
        _scopeFactory = scopeFactory;
        _scheduler = scheduler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_scheduler.External) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync();
            }
            catch
            {
                // כשל בסבב בודד לא מפיל את השירות
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // השרת נסגר
            }
        }
    }

    public async Task RunAsync()
    {
        await Gate.WaitAsync();
        try
        {
            var nowIsrael = IsraelTime.Now;
            if (nowIsrael.Hour < 9 || nowIsrael.Hour >= 20)
                return;
            if (DateTime.UtcNow - _lastRunUtc < TimeSpan.FromMinutes(55))
                return;
            _lastRunUtc = DateTime.UtcNow;

            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IStaleItemService>().SendNoticesAsync();
        }
        finally
        {
            Gate.Release();
        }
    }
}
