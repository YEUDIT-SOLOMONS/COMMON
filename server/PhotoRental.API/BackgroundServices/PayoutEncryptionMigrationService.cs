namespace PhotoRental.API.BackgroundServices;

using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Security;

// רץ פעם אחת בעליית השרת: מצפין מספרי חשבון בנק שנשמרו כטקסט גלוי לפני שההצפנה הופעלה
// (ר' FieldEncryptor). כל ריצה בודקת רק ערכים שעדיין לא מוצפנים, כך שאחרי הריצה הראשונה
// עם מפתח מוגדר היא לא משנה כלום. כשל כאן נרשם ביומן בלבד ולא מפיל את השרת.
public class PayoutEncryptionMigrationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly FieldEncryptor _encryptor;
    private readonly ILogger<PayoutEncryptionMigrationService> _logger;

    public PayoutEncryptionMigrationService(
        IServiceScopeFactory scopeFactory,
        FieldEncryptor encryptor,
        ILogger<PayoutEncryptionMigrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _encryptor = encryptor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_encryptor.IsEnabled)
        {
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            var updated = await users.EncryptLegacyPayoutAccountNumbersAsync();
            if (updated > 0)
            {
                _logger.LogInformation("הוצפנו {Count} מספרי חשבון בנק שנשמרו בעבר כטקסט גלוי.", updated);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "הצפנת מספרי חשבון ישנים נכשלה - תנוסה שוב בעליית השרת הבאה.");
        }
    }
}
