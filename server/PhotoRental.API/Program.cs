using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PhotoRental.API.BackgroundServices;
using PhotoRental.API.Middlewares;
using PhotoRental.API.Security;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;
using PhotoRental.Core.Security;
using PhotoRental.Data;
using PhotoRental.Data.Repositories;
using PhotoRental.Service.Seeding;
using PhotoRental.Service.Services;
using NLog;
using NLog.Web;

// ===================== יומן רישום (Logging) - NLog =====================
// ההגדרות ב-nlog.config. הקוד עצמו משתמש ב-ILogger הרגיל של .NET, ו-NLog מתחבר מתחת
var logger = LogManager.Setup().LoadConfigurationFromFile(Path.Combine(AppContext.BaseDirectory, "nlog.config")).GetCurrentClassLogger();

try
{
    logger.Info("PhotoRental.API עולה...");

    // עקיפת פרוקסי המערכת בררת המחדל: השגיאה "Unable to establish HTTP/2 connection" מול Firestore
    // נגרמת כמעט תמיד מפרוקסי מקומי (למשל תוכנת אנטי-וירוס עם סריקת HTTPS, VPN, או פרוקסי ארגוני)
    // שמיירט את חיבור ה-HTTPS ואינו תומך במשא ומתן HTTP/2 - למרות ש-Google Cloud תומך בו במלואו.
    // ביטול הפרוקסי בררת המחדל לכל בקשות ה-HTTP באפליקציה (חייב לקרות לפני יצירת כל HttpClient) עוקף זאת.
    HttpClient.DefaultProxy = new WebProxy();

    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Host.UseNLog();

    // טעינת הגדרות החיבור ל-Firebase ואימות שהן קיימות
    var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
    var firebaseCredentialsPath = builder.Configuration["Firebase:CredentialsPath"];
    // בפיתוח מקומי עובדים מול Firestore Emulator (למשל http://localhost:8080) - בלי קובץ מפתח ובלי ענן
    var firebaseEmulatorHost = builder.Configuration["Firebase:EmulatorHost"];
    var useEmulator = !string.IsNullOrWhiteSpace(firebaseEmulatorHost);

    if (string.IsNullOrEmpty(firebaseProjectId) || (!useEmulator && string.IsNullOrEmpty(firebaseCredentialsPath)))
    {
        throw new InvalidOperationException("Missing Firebase configuration in appsettings or User Secrets.");
    }

    if (!useEmulator && !Path.IsPathRooted(firebaseCredentialsPath))
    {
        firebaseCredentialsPath = Path.Combine(builder.Environment.ContentRootPath, firebaseCredentialsPath!);
    }

    // ===================== רישום שירותי הזרקת התלויות (Dependency Injection) =====================

    // חיבור יחיד (Singleton) ל-Firestore, משותף לכל בקשות האפליקציה.
    // החיבור נוצר באופן עצל (רק כשמגיעה בקשת API ראשונה שזקוקה לו) ולא כאן בעליית האפליקציה -
    // כדי שבעיית התחברות ל-Firebase לא תפיל את כל השרת (מה שמונע מ-Swagger לעלות בכלל),
    // אלא תוצג כתשובת שגיאה מפורטת וברורה בבקשה עצמה (דרך ה-Middleware הגלובלי), הניתנת לצפייה בדפדפן.
    builder.Services.AddSingleton(_ => useEmulator
        ? new FirebaseDbContext(firebaseProjectId, new Uri(firebaseEmulatorHost!.StartsWith("http") ? firebaseEmulatorHost : $"http://{firebaseEmulatorHost}"))
        : new FirebaseDbContext(firebaseProjectId, firebaseCredentialsPath!));

    // הצפנת מספרי חשבון הבנק של המשכירים במסד הנתונים (חוק הגנת הפרטיות / תקנות אבטחת מידע) -
    // ר' הערה מפורטת ב-FieldEncryptor. המפתח מגיע ממשתנה הסביבה Encryption__PayoutKey (ב-Cloud Run)
    // או מ-User Secrets בפיתוח, ולעולם לא נשמר בקוד/ב-appsettings שבמאגר.
    var payoutEncryptor = new FieldEncryptor(builder.Configuration["Encryption:PayoutKey"]);
    if (!payoutEncryptor.IsEnabled)
    {
        logger.Warn("Encryption:PayoutKey לא הוגדר - מספרי חשבון הבנק נשמרים ללא הצפנה. יש להגדיר מפתח בהקדם.");
    }
    builder.Services.AddSingleton(payoutEncryptor);
    builder.Services.AddHostedService<PayoutEncryptionMigrationService>();

    // שכבת הגישה לנתונים (Repositories) - Scoped: מופע חדש לכל בקשת HTTP
    builder.Services.AddScoped<IItemRepository, ItemRepository>();
    builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<ILoanRepository, LoanRepository>();
    builder.Services.AddScoped<IContactMessageRepository, ContactMessageRepository>();

    // שכבת הלוגיקה העסקית (Services)
    builder.Services.AddAutoMapper(cfg => cfg.LicenseKey = builder.Configuration["AutoMapper:LicenseKey"], typeof(PhotoRental.Service.Mapping.MappingProfile));

    // migrations של נתונים (המקבילה של EF Migrations) - רצות בעליית השרת, כל אחת פעם אחת
    builder.Services.AddSingleton<PhotoRental.Data.Migrations.IDataMigration, PhotoRental.Data.Migrations.M0001_BackfillItemQueryFields>();
    builder.Services.AddSingleton<PhotoRental.Data.Migrations.IDataMigration, PhotoRental.Data.Migrations.M0002_AddItemVersion>();
    builder.Services.AddSingleton<PhotoRental.Data.Migrations.DataMigrationRunner>();
    builder.Services.AddScoped<DemoDataSeeder>();

    builder.Services.AddScoped<IItemService, ItemService>();
    builder.Services.AddScoped<ICategoryService, CategoryService>();
    builder.Services.AddScoped<IAdminService, AdminService>();
    builder.Services.AddScoped<IUserService, UserService>();
    builder.Services.AddScoped<ILoanService, LoanService>();
    builder.Services.AddScoped<IContactMessageService, ContactMessageService>();

    // התחברות עם Google מתבצעת דרך Firebase Authentication בצד הלקוח (ולא מול Google ישירות) -
    // ראו הערה מפורטת ב-AuthService.LoginWithGoogleAsync. משתמשים באותו מזהה פרויקט Firebase
    // שכבר נקרא למעלה עבור חיבור Firestore (firebaseProjectId) - אין צורך בהגדרה נוספת כלשהי.
    builder.Services.AddSingleton(new FirebaseAuthOptions { ProjectId = firebaseProjectId });
    builder.Services.AddHttpClient<IAuthService, AuthService>();

    // שירות אימות הזמינות הטלפוני - אם הוגדרו פרטי Twilio אמיתיים (Twilio:AccountSid/AuthToken/
    // FromPhoneNumber/PublicBaseUrl ב-appsettings.json, ראו הערה מפורטת ב-TwilioAvailabilityCallService)
    // מתבצע חיוג טלפוני אמיתי; אחרת (בררת המחדל) ממשיכים עם השירות המדומה
    // (SimulatedAvailabilityCallService). TwilioOptions נרשם תמיד (גם ריק) כי ה-Controller תלוי בו
    // ישירות לצורך אימות חתימת ה-Webhook. נרשם כ-Scoped (לא Singleton) כדי לא ליצור תלות "שבויה"
    // (Captive Dependency) מול ה-Repositories שהם עצמם Scoped; מצב השיחות עצמו משותף בין הבקשות
    // דרך שדה Static בתוך השירות.
    var twilioOptions = new TwilioOptions
    {
        AccountSid = builder.Configuration["Twilio:AccountSid"] ?? string.Empty,
        AuthToken = builder.Configuration["Twilio:AuthToken"] ?? string.Empty,
        FromPhoneNumber = builder.Configuration["Twilio:FromPhoneNumber"] ?? string.Empty,
        PublicBaseUrl = builder.Configuration["Twilio:PublicBaseUrl"] ?? string.Empty,
        CommonNumber = builder.Configuration["Twilio:CommonNumber"] ?? string.Empty
    };
    builder.Services.AddSingleton(twilioOptions);

    // קול הקריין בשיחות (חייב להיות קול עברי של גוגל; ברירת מחדל Google.he-IL-Wavenet-A)
    var twilioVoice = builder.Configuration["Twilio:Voice"];
    if (!string.IsNullOrWhiteSpace(twilioVoice))
        PhotoRental.Service.Services.CallTwiml.Voice = twilioVoice.Trim();

    // תשלום עמלה באשראי (SUMIT) - פעיל רק כשהוגדרו מזהה העסק והמפתח הפרטי (Summit:CompanyId, Summit:ApiKey)
    var summitApiKey = builder.Configuration["Summit:ApiKey"];
    if (long.TryParse(builder.Configuration["Summit:CompanyId"], out var summitCompanyId) && summitCompanyId > 0 && !string.IsNullOrWhiteSpace(summitApiKey))
    {
        builder.Services.AddSingleton(new SummitOptions
        {
            CompanyId = summitCompanyId,
            ApiKey = summitApiKey,
            PublicKey = builder.Configuration["Summit:PublicKey"] ?? string.Empty,
            BaseUrl = builder.Configuration["Summit:BaseUrl"] ?? "https://api.sumit.co.il",
            // סוג המסמך אחרי תשלום: ברירת מחדל 1 (חשבונית מס/קבלה, אצל עוסק פטור חשבון/קבלה); Summit:DocumentType=2 לקבלה בלבד
            DocumentType = int.TryParse(builder.Configuration["Summit:DocumentType"], out var summitDocType) ? summitDocType : 1
        });
        builder.Services.AddHttpClient<ICardPaymentService, SummitCardPaymentService>();
    }
    else
    {
        builder.Services.AddSingleton<ICardPaymentService, NullCardPaymentService>();
    }

    // עמלת השירות (8% כברירת מחדל) - ראו CommissionService
    builder.Services.AddSingleton(new CommissionOptions
    {
        Percent = decimal.TryParse(builder.Configuration["Commission:Percent"], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var commissionPercent)
            ? commissionPercent
            : 8m,
        FirstBillingMonth = builder.Configuration["Commission:FirstBillingMonth"] ?? "2026-10",
        PaymentInstructions = builder.Configuration["Commission:PaymentInstructions"] ?? string.Empty,
        PaymentLinkUrl = builder.Configuration["Commission:PaymentLinkUrl"] ?? string.Empty,
        BankDetails = builder.Configuration["Commission:BankDetails"] ?? string.Empty
    });
    builder.Services.AddScoped<ICommissionStatementRepository, CommissionStatementRepository>();
    builder.Services.AddScoped<ICallLogRepository, CallLogRepository>();
    builder.Services.AddScoped<IOutboundCallService, OutboundCallService>();
    builder.Services.AddScoped<ILoanExtensionService, LoanExtensionService>();
    builder.Services.AddScoped<IPartyBridgeService, PartyBridgeService>();
    builder.Services.AddSingleton(new ShippingChargeOptions { Enabled = string.Equals(builder.Configuration["Shipping:ChargeEnabled"], "true", StringComparison.OrdinalIgnoreCase) });
    builder.Services.AddScoped<IShippingPaymentService, ShippingPaymentService>();
    builder.Services.AddScoped<IExtraChargeService, ExtraChargeService>();
    builder.Services.AddScoped<IOrderPaymentService, OrderPaymentService>();
    builder.Services.AddScoped<IPayoutService, PayoutService>();
    builder.Services.AddScoped<ICommissionDebtService, CommissionDebtService>();
    builder.Services.AddHttpClient<IAddressDirectory, AddressDirectory>();
    builder.Services.AddHttpClient<ICityCoordsService, CityCoordsService>();
    builder.Services.AddScoped<ICommissionService, CommissionService>();
    builder.Services.AddScoped<IPaymentProofRepository, PaymentProofRepository>();
    builder.Services.AddScoped<IPaymentDraftRepository, PaymentDraftRepository>();
    builder.Services.AddScoped<IFavoriteRepository, FavoriteRepository>();
    builder.Services.AddScoped<IReviewRepository, ReviewRepository>();
    builder.Services.AddScoped<IMarkerRepository, MarkerRepository>();
    builder.Services.AddScoped<IItemBlockRepository, ItemBlockRepository>();
    builder.Services.AddScoped<IAvailabilityRequestRepository, AvailabilityRequestRepository>();
    builder.Services.AddScoped<IItemQuestionRepository, ItemQuestionRepository>();
    builder.Services.AddScoped<ISaleRepository, SaleRepository>();
    builder.Services.AddScoped<PhotoRental.Service.Services.SaleService>();
    builder.Services.AddScoped<IVisitRepository, VisitRepository>();
    builder.Services.AddScoped<ICityCoordsRepository, CityCoordsRepository>();
    builder.Services.AddScoped<IStreetNeighborhoodRepository, StreetNeighborhoodRepository>();
    builder.Services.AddScoped<IItemScheduleService, ItemScheduleService>();
    builder.Services.AddScoped<IFavoriteService, FavoriteService>();
    builder.Services.AddScoped<IReviewService, ReviewService>();
    builder.Services.AddScoped<IPickupReminderService, PickupReminderService>();

    if (!string.IsNullOrWhiteSpace(twilioOptions.AccountSid) &&
        !string.IsNullOrWhiteSpace(twilioOptions.AuthToken) &&
        !string.IsNullOrWhiteSpace(twilioOptions.FromPhoneNumber) &&
        !string.IsNullOrWhiteSpace(twilioOptions.PublicBaseUrl))
    {
        builder.Services.AddHttpClient<IAvailabilityCallService, TwilioAvailabilityCallService>();
    }
    else
    {
        builder.Services.AddScoped<IAvailabilityCallService, SimulatedAvailabilityCallService>();
    }

    // תזכורת החזרה בשיחת טלפון (שעתיים לפני סיום ההשכרה) - חיוג אמיתי דרך Twilio אם הוגדרו SID/אסימון/מספר
    // מתקשר (אותה הגדרה כמו אימות הזמינות; Twilio:PublicBaseUrl אינו נדרש כאן), אחרת רק מייל
    if (!string.IsNullOrWhiteSpace(twilioOptions.AccountSid) &&
        !string.IsNullOrWhiteSpace(twilioOptions.AuthToken) &&
        !string.IsNullOrWhiteSpace(twilioOptions.FromPhoneNumber))
    {
        builder.Services.AddHttpClient<IReminderCallService, TwilioReminderCallService>();
    }
    else
    {
        builder.Services.AddSingleton<IReminderCallService, NullReminderCallService>();
    }

    // שירות שילוח - אם הוגדר מפתח API אמיתי (GetPackage:ApiKey ב-appsettings.json, ראו הערה
    // מפורטת ב-GetPackageShippingService.cs) נעשה שימוש בחיבור האמיתי אל GetPackage; אחרת
    // ממשיכים עם השירות המדומה (SimulatedShippingService).
    var getPackageApiKey = builder.Configuration["GetPackage:ApiKey"];
    if (!string.IsNullOrWhiteSpace(getPackageApiKey))
    {
        builder.Services.AddSingleton(new GetPackageOptions
        {
            ApiKey = getPackageApiKey,
            BaseUrl = builder.Configuration["GetPackage:BaseUrl"] ?? "https://sandbox-apiv2.getpackage.com"
        });
        builder.Services.AddHttpClient<IShippingService, GetPackageShippingService>();
    }
    else
    {
        builder.Services.AddScoped<IShippingService, SimulatedShippingService>();
    }

    // כתובת בסיס האתר - משמשת לבניית קישורים מוחלטים בתוך אימיילים (כגון קישור איפוס סיסמה)
    builder.Services.AddSingleton(new ClientOptions
    {
        BaseUrl = builder.Configuration["Client:BaseUrl"] ?? "http://localhost:5173"
    });

    // שליחת אימיילים אמיתיים דרך Resend - אם הוגדר מפתח API אמיתי (Resend:ApiKey ב-appsettings.json,
    // ראו הערה מפורטת ב-ResendEmailService.cs) נעשה שימוש בחיבור האמיתי; אחרת ממשיכים עם
    // NullEmailService (לא שולח בפועל, אך גם לא נכשל - ראו הערה שם).
    var resendApiKey = builder.Configuration["Resend:ApiKey"];
    if (!string.IsNullOrWhiteSpace(resendApiKey))
    {
        builder.Services.AddSingleton(new ResendOptions
        {
            ApiKey = resendApiKey,
            FromEmail = builder.Configuration["Resend:FromEmail"] ?? string.Empty,
            ReplyTo = builder.Configuration["Resend:ReplyTo"] ?? builder.Configuration["Resend:FromEmail"] ?? string.Empty,
            FromName = builder.Configuration["Resend:FromName"] ?? "common"
        });
        builder.Services.AddHttpClient<IEmailService, ResendEmailService>();
    }
    else
    {
        builder.Services.AddSingleton<IEmailService, NullEmailService>();
    }

    // תזכורת אוטומטית להחזרת ציוד - שירות רקע שרץ לאורך כל חיי השרת (ראו הערה מפורטת ב-
    // ReturnReminderBackgroundService), ובודק כל שעה אם יש השכרות שמועד ההחזרה שלהן קרב.
    // נרשמים גם כ-Singleton רגיל כדי ש-SchedulerController יוכל להפעיל סבב לפי דרישה (מ-Cloud Scheduler) -
    // כך התזכורות רצות בזמן גם כשהשרת "ישן" בין בקשות, בלי אינסטנציה פעילה תמיד
    // הגבלת קצב לפי כתובת IP של הפונה (Cloud Run מוסיף את הכתובת האמיתית בסוף X-Forwarded-For). מגבלות נדיבות, כי
    // כמה משתמשים עשויים לשתף כתובת (רשת סינון, ספק סלולר)
    static string ClientIp(HttpContext ctx)
    {
        var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var last = forwarded.Split(',')[^1].Trim();
            if (last.Length > 0) return last;
        }

        return ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    builder.Services.AddRateLimiter(limiter =>
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.OnRejected = async (context, token) =>
        {
            context.HttpContext.Response.ContentType = "application/json";
            await context.HttpContext.Response.WriteAsync(
                "{\"StatusCode\":429,\"Message\":\"יותר מדי בקשות. אפשר לנסות שוב בעוד כמה דקות.\"}", token);
        };
        limiter.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
            "login:" + ClientIp(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        limiter.AddPolicy("signup", ctx => RateLimitPartition.GetFixedWindowLimiter(
            "signup:" + ClientIp(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
        limiter.AddPolicy("contact", ctx => RateLimitPartition.GetFixedWindowLimiter(
            "contact:" + ClientIp(ctx),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
    });

    builder.Services.AddSingleton(new SchedulerOptions(!string.IsNullOrEmpty(builder.Configuration["Scheduler:Key"])));
    builder.Services.AddSingleton<ReturnReminderBackgroundService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ReturnReminderBackgroundService>());
    builder.Services.AddSingleton<MonthlyCommissionBackgroundService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<MonthlyCommissionBackgroundService>());
    builder.Services.AddScoped<IStaleItemService, StaleItemService>();
    builder.Services.AddSingleton<StaleItemBackgroundService>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<StaleItemBackgroundService>());

    // ===================== אימות והרשאה (JWT) =====================
    // הנפקת הטוקן עצמה (JwtTokenGenerator) נמצאת בשכבת ה-API, אך פרמטרי האימות (מפתח/מנפיק/קהל יעד)
    // חייבים להיות זהים בין ההנפקה (למטה) לבין ה-Middleware שמאמת את הטוקן בכל בקשה (AddJwtBearer),
    // ולכן שניהם נטענים כאן מאותו מקור הגדרות (appsettings.json -> Jwt).
    // המפתח לא נמצא באף קובץ ב-repo: בפיתוח - User Secrets, בייצור - משתנה סביבה Jwt__Key.
    // אם לא הוגדר בפיתוח, נוצר מפתח זמני כדי שהפרויקט ירוץ מיד (טוקנים יפוגו באתחול הבא)
    if (string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Key"]) && builder.Environment.IsDevelopment())
    {
        builder.Configuration["Jwt:Key"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
        logger.Warn("Jwt:Key לא הוגדר ב-User Secrets - נוצר מפתח זמני לפיתוח");
    }

    builder.Services.AddOptions<JwtOptions>()
        .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
        .Validate(o => o.Key.Length >= 32, "Jwt:Key חייב להיות באורך 32 תווים לפחות")
        .ValidateOnStart();

    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    if (string.IsNullOrWhiteSpace(jwtOptions.Key))
    {
        throw new InvalidOperationException("Missing Jwt:Key configuration (User Secrets or environment variable Jwt__Key).");
    }

    builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        });

    builder.Services.AddAuthorization();

    // מדיניות CORS - מאפשרת לאתר הלקוח (Frontend) של השכרת ציוד הצילום להתחבר ל-API.
    // המקורות המורשים נטענים מתוך ההגדרות (Cors:AllowedOrigins ב-appsettings);
    // אם לא הוגדרו מקורות (למשל בסביבת פיתוח מקומית) - נאפשר כל מקור, כדי לא לחסום את פיתוח הלקוח.
    const string CorsPolicyName = "PhotoRentalClient";
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy(CorsPolicyName, policy =>
        {
            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins)
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
            else
            {
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
        });
    });

    builder.Services
        .AddControllers(options => options.Filters.Add<PhotoRental.API.Middlewares.UniformErrorResultFilter>())
        .ConfigureApiBehaviorOptions(options =>
        {
            // ModelState לא תקין (Data Annotations על ה-DTO) -> 400 באותו מבנה JSON של שאר השגיאות
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(e => e.Value?.Errors.Count > 0)
                    .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());
                var first = errors.Values.SelectMany(v => v).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

                return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
                {
                    StatusCode = 400,
                    Message = first ?? "הבקשה אינה תקינה",
                    Errors = errors,
                    CorrelationId = context.HttpContext.GetCorrelationId()
                });
            };
        })
        .AddJsonOptions(options =>
        {
            // הצגת ערכי Enum כטקסט קריא ב-JSON (למשל "Available" ולא 0),
            // כך שהלקוח מקבל תשובה ברורה וניתנת לקריאה במקום מספרים.
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

    // Swagger / OpenAPI - מסמך API אינטראקטיבי לבדיקה ואימות שכל הנקודות (Endpoints) תקינות
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "PhotoRental API",
            Version = "v1",
            Description = "API לניהול אתר השכרת ציוד צילום - פריטים, קטגוריות, משתמשים, השכרות ואימות."
        });

        // הגדרת תמיכה בטוקן JWT ב-Swagger UI, כדי שניתן יהיה לבדוק נקודות מוגנות ישירות מהדפדפן:
        // מתקבל טוקן מ- POST /api/auth/login או /api/auth/register, ואז לוחצים על "Authorize" ומדביקים אותו.
        var jwtSecurityScheme = new OpenApiSecurityScheme
        {
            Scheme = "bearer",
            BearerFormat = "JWT",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Description = "הזינו את הטוקן שהתקבל מ- /api/auth/login או /api/auth/register (ללא המילה \"Bearer\" - Swagger מוסיף זאת אוטומטית)",
            Reference = new OpenApiReference
            {
                Id = JwtBearerDefaults.AuthenticationScheme,
                Type = ReferenceType.SecurityScheme
            }
        };

        options.AddSecurityDefinition(jwtSecurityScheme.Reference.Id, jwtSecurityScheme);
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            { jwtSecurityScheme, Array.Empty<string>() }
        });
    });

    var app = builder.Build();

    // טעינה מוקדמת של מדריך הכתובות הממשלתי בהפעלת השרת, כדי שההשלמה האוטומטית תהיה מהירה כבר מההקלדה הראשונה
    app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
    {
        try
        {
            using var scope = app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IAddressDirectory>().SearchCitiesAsync("א", 1);
        }
        catch
        {
            // ייטען בבקשה הראשונה
        }
    }));

    // ===================== צנרת הבקשות (Middleware Pipeline) =====================

    // טיפול גלובלי ומרוכז בשגיאות - חייב להיות הראשון בצנרת כדי לתפוס חריגות מכל שלב שאחריו
    app.UseGlobalExceptionHandling();

    // CorrelationId לכל בקשה + שורת לוג לכל בקשה נכנסת
    app.UseCorrelationId();

    // חשיפת Swagger UI בסביבת פיתוח בלבד, לצורך בדיקה נוחה של כל ה-API מהדפדפן
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "PhotoRental API v1");
        });
    }

    // ב-Cloud Run ה-HTTPS כבר מסתיים (terminated) בשכבת הרשת של גוגל לפני שהבקשה מגיעה לקונטיינר -
    // כלומר בפנים היא תמיד נראית כ-HTTP רגיל, ו-UseHttpsRedirection היה גורם ללולאת הפניות אינסופית.
    // K_SERVICE הוא משתנה סביבה שגוגל מגדירה אוטומטית בכל שירות Cloud Run - קיומו מזהה את הסביבה הזו.
    var isCloudRun = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("K_SERVICE"));
    if (!isCloudRun)
    {
        app.UseHttpsRedirection();
    }
    app.UseCors(CorsPolicyName);
    app.UseRateLimiter();

    // UseAuthentication (זיהוי מי המשתמש, לפי טוקן ה-JWT) חייב להגיע לפני UseAuthorization (בדיקת הרשאות)
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    // migrations ואז זריעה: קטגוריות תמיד, נתוני דמו רק כש-Seed:DemoData=true. רץ ברקע כדי שתקלה
    // בבסיס הנתונים לא תמנע מ-Swagger לעלות
    var seedDemoData = app.Configuration.GetValue<bool>("Seed:DemoData");
    _ = Task.Run(async () =>
    {
        try
        {
            var ran = await app.Services.GetRequiredService<PhotoRental.Data.Migrations.DataMigrationRunner>().MigrateAsync();
            app.Logger.LogInformation("Migrations: {Count} חדשות ({Ids})", ran.Count, string.Join(", ", ran));

            using var scope = app.Services.CreateScope();
            await new CategorySeeder(scope.ServiceProvider.GetRequiredService<ICategoryRepository>()).SeedAsync();

            if (seedDemoData && await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync())
                app.Logger.LogInformation("נזרעו נתוני דמו (משתמשות ופריטים)");
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "migrations או זריעת נתונים נכשלו - ניתן לנסות שוב באתחול הבא של השרת");
        }
    });

    app.Run();
}
catch (Exception ex)
{
    logger.Fatal(ex, "PhotoRental.API נכשל בעליה");
    throw;
}
finally
{
    LogManager.Shutdown();
}

public partial class Program;
