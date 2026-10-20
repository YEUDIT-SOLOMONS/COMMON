namespace PhotoRental.API.BackgroundServices;

using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;
using PhotoRental.Service.Services;

// שולח אוטומטית, החל מ-1 בכל חודש (אחרי 08:00 שעון ישראל), את דוח העמלה של החודש שחלף לכל משכירה שיש לה
// עמלה ועוד לא נשלח לה דוח. הבדיקה רצה כל שעה והיא אידמפוטנטית (נשלח פעם אחת לכל משכירה לחודש).
// חודשים שקדמו ל-Commission:FirstBillingMonth לא נשלחים, כדי לא לחייב רטרואקטיבית.
public class MonthlyCommissionBackgroundService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CommissionOptions _options;
    private readonly SchedulerOptions _scheduler;
    private static DateTime _lastRunUtc = DateTime.MinValue;

    public MonthlyCommissionBackgroundService(IServiceScopeFactory scopeFactory, CommissionOptions options, SchedulerOptions scheduler)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _scheduler = scheduler;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_scheduler.External) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendPreviousMonthAsync();
            }
            catch
            {
                // כשל בסבב בודד (למשל בעיית רשת רגעית) לא מפיל את השירות - מנסים שוב בסבב הבא
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

    // נקרא מהלולאה הפנימית, וגם מ-POST /api/internal/scheduled-runs (ראו SchedulerController)
    public async Task SendPreviousMonthAsync()
    {
        if (DateTime.UtcNow - _lastRunUtc < TimeSpan.FromHours(1))
            return;

        await Gate.WaitAsync();
        try
        {
            _lastRunUtc = DateTime.UtcNow;
            await SendPreviousMonthCoreAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task SendPreviousMonthCoreAsync()
    {
        var nowIsrael = CommissionService.ToIsraelTime(DateTime.UtcNow);
        if (nowIsrael.Hour < 8)
            return;

        var previousMonth = CommissionService.MonthKey(new DateTime(nowIsrael.Year, nowIsrael.Month, 1).AddMonths(-1));
        if (string.CompareOrdinal(previousMonth, _options.FirstBillingMonth) < 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        if (!emailService.IsEnabled)
            return;

        var commissionService = scope.ServiceProvider.GetRequiredService<ICommissionService>();
        await commissionService.SendMonthlyReportsAsync(previousMonth);
    }
}
