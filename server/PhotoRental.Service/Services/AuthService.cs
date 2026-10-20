namespace PhotoRental.Service.Services;

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;
using PhotoRental.Core.Security;

// שכבת הלוגיקה העסקית לאימות משתמשים: הרשמה (יצירת משתמש + גיבוב סיסמה) והתחברות (אימות סיסמה מול הגיבוב).
// אינה מנפיקה טוקן JWT בעצמה - מחזירה את ישות המשתמש המאומת, וה-API (AuthController) הוא שמנפיק את הטוקן.
public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IEmailService _emailService;
    private readonly ClientOptions _clientOptions;
    private readonly HttpClient _http;
    private readonly FirebaseAuthOptions _firebaseAuthOptions;
    private readonly IOutboundCallService _calls;

    public AuthService(
        IUserRepository userRepository,
        IEmailService emailService,
        ClientOptions clientOptions,
        HttpClient http,
        FirebaseAuthOptions firebaseAuthOptions,
        IOutboundCallService calls)
    {
        _userRepository = userRepository;
        _emailService = emailService;
        _clientOptions = clientOptions;
        _http = http;
        _firebaseAuthOptions = firebaseAuthOptions;
        _calls = calls;
    }

    public async Task<User> RegisterAsync(RegisterDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם מלא");
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ArgumentException("יש להזין כתובת אימייל");
        RequireStrongPassword(dto.Password);
        if (!dto.TermsAccepted)
            throw new ArgumentException("יש לאשר את תנאי השימוש כדי להירשם");
        dto.Email = dto.Email.Trim().ToLowerInvariant();

        if (await _userRepository.GetByEmailAsync(dto.Email) is not null)
            throw new InvalidOperationException("קיים כבר משתמש עם כתובת אימייל זו");

        // חשבון אחד לכל מספר טלפון - כדי שלא ייפתח חשבון שני בטעות (למשל כשהדפדפן מילא מייל אחר)
        var phoneKey = PhoneKey(dto.Phone);
        if (phoneKey.Length >= 9 &&
            (await _userRepository.GetAllAsync()).FirstOrDefault(u => PhoneKey(u.Phone) == phoneKey) is { } existing)
            throw new InvalidOperationException(
                $"כבר יש חשבון עם מספר הטלפון הזה (במייל {MaskEmail(existing.Email)}). אפשר להתחבר אליו, ואם הסיסמה לא זכורה - דרך \"שכחתי סיסמה\".");

        // אין הבחנה בין "שוכר" ל"משכיר" - כל חשבון יכול גם לשכור וגם לשתף ציוד משלו, ולכן כל
        // הרשמה חדשה מקבלת את התפקיד Owner (המעניק גישה מלאה לשתי היכולות באזור האישי בצד הלקוח).
        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email,
            Phone = dto.Phone,
            Role = UserRole.Owner,
            CreatedAtUtc = DateTime.UtcNow,
            TermsAcceptedAtUtc = DateTime.UtcNow,
            TermsVersion = TermsInfo.CurrentVersion,
            PasswordHash = PasswordHasher.Hash(dto.Password),
            QuietHoursEnabled = dto.QuietHoursEnabled,
            QuietFrom = QuietHours.Normalize(dto.QuietFrom) ?? QuietHours.DefaultFrom,
            QuietTo = QuietHours.Normalize(dto.QuietTo) ?? QuietHours.DefaultTo
        };

        var created = await _userRepository.CreateAsync(user);
        await Task.WhenAll(SendWelcomeEmailAsync(created), PlaceWelcomeCallAsync(created));
        return created;
    }

    private async Task PlaceWelcomeCallAsync(User user)
    {
        try
        {
            await _calls.CallWelcomeAsync(user);
        }
        catch
        {
            // שיחה שלא יצאה אינה סיבה לבטל הרשמה
        }
    }

    // לפחות 6 תווים, ובהם גם אותיות וגם ספרות. חל רק על סיסמאות חדשות - סיסמאות קיימות ממשיכות לעבוד
    private static void RequireStrongPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 6 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new ArgumentException("הסיסמה צריכה לכלול לפחות 6 תווים, משולבים אותיות ומספרים");
    }

    // מייל "ברוכה הבאה" לחברה חדשה (אחרי הרשמה רגילה או כניסה ראשונה עם גוגל). כשל במייל לא מכשיל את ההרשמה.
    private async Task SendWelcomeEmailAsync(User user)
    {
        if (!_emailService.IsEnabled || string.IsNullOrWhiteSpace(user.Email))
            return;

        try
        {
            var baseUrl = _clientOptions.BaseUrl.TrimEnd('/');
            string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            const string button = "display:inline-block;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none";
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>שלום {H(NameText.FirstName(user.Name))},</p>" +
                       "<p><strong>ברוכה הבאה ל-common!</strong> קהילת הצלמות להשכרת ציוד צילום.</p>" +
                       "<p>מה אפשר לעשות כאן:</p>" +
                       "<ul>" +
                       "<li>להשכיר ציוד מחברות הקהילה: נכנסים לקטלוג, בוחרים פריט ומזמינים.</li>" +
                       "<li>להשכיר את הציוד שלך: לוחצים על \"+ שיתוף ציוד חדש\" ומוסיפים פריט (אפשר גם כמה תמונות).</li>" +
                       "</ul>" +
                       $"<p><a href=\"{baseUrl}\" style=\"background:#a8e29c;{button}\"><strong>לקטלוג הציוד</strong></a> " +
                       $"<a href=\"{baseUrl}/profile\" style=\"background:#fff8f8;{button}\">לאזור האישי</a></p>" +
                       "<p>אם יש שאלה, אפשר להשיב למייל הזה.</p>" +
                       "<p>בהצלחה,<br/>common</p></div>";
            await _emailService.SendAsync(user.Email, user.Name, "ברוכה הבאה ל-common!", html);
        }
        catch
        {
            // מייל שלא נשלח אינו סיבה לבטל הרשמה
        }
    }

    // מעקב למנהלות: זמן ההתחברות האחרונה. כישלון בשמירה לא מונע התחברות.
    private async Task RecordLoginAsync(User user)
    {
        try
        {
            user.LastLoginUtc = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(user.Id))
                await _userRepository.UpdateLastLoginAsync(user.Id, user.LastLoginUtc.Value);
        }
        catch
        {
            // לא חוסמים התחברות בגלל תקלה בשמירת חותמת הזמן
        }
    }

    private static readonly LoginThrottle Throttle = new();

    public async Task<User> LoginAsync(LoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
            throw new ArgumentException("יש להזין אימייל וסיסמה");
        dto.Email = dto.Email.Trim().ToLowerInvariant();

        if (Throttle.IsLocked(dto.Email, out var remaining))
        {
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            throw new UnauthorizedAccessException(
                $"יותר מדי ניסיונות התחברות שגויים. אפשר לנסות שוב בעוד {minutes} דקות, או לאפס את הסיסמה.");
        }

        var user = await _userRepository.GetByEmailAsync(dto.Email);

        if (user is not null && string.IsNullOrEmpty(user.PasswordHash))
            throw new UnauthorizedAccessException("החשבון הזה נוצר דרך Google. אפשר להתחבר עם Google, או ללחוץ על \"שכחתי סיסמה\" כדי להגדיר סיסמה.");

        if (user is null || !PasswordHasher.Verify(dto.Password, user.PasswordHash!))
        {
            Throttle.RecordFailure(dto.Email);
            throw new UnauthorizedAccessException("אימייל או סיסמה שגויים");
        }

        Throttle.Reset(dto.Email);
        await RecordLoginAsync(user);
        return user;
    }

    // כתובת המפתחות הציבוריים של Google לאימות אסימוני Firebase Authentication (שונה מכתובת
    // המפתחות הרגילה של Google Sign-In הישיר - זו הכתובת הספציפית לשירות "securetoken" של Firebase)
    private const string FirebaseCertsUrl =
        "https://www.googleapis.com/service_accounts/v1/metadata/x509/securetoken@system.gserviceaccount.com";

    // מטמון פשוט של המפתחות הציבוריים בזיכרון התהליך (משותף בין כל הבקשות, למרות ש-AuthService
    // עצמו Scoped - אותו עיקרון בדיוק שבו משתמש SimulatedAvailabilityCallService) כדי לא לפנות
    // ל-Google בכל התחברות; מתרענן אוטומטית כל שעה, בדיוק כמו שהמפתחות עצמם מתחלפים מצד גוגל מדי כמה שעות.
    private static readonly object CertsCacheLock = new();
    private static Dictionary<string, string>? _cachedCerts;
    private static DateTime _certsCachedAtUtc;

    // התחברות (או הרשמה אוטומטית, בפעם הראשונה) באמצעות Google - דרך Firebase Authentication.
    // הלקוח מבצע את זרימת ההתחברות מול Google בעצמו (דרך ה-SDK של Firebase, ראו firebase.js
    // בצד הלקוח) ומקבל בחזרה אסימון זהות (ID token) שחתום ע"י שירות "securetoken" של Firebase -
    // לא אסימון Google גולמי. השרת כאן מאמת את האסימון בעצמו (ולא סומך על תוכנו כפי שהוא, שהרי
    // כל אחד יכול לשלוח JSON מזויף): מוריד את המפתח הציבורי המתאים מגוגל, מוודא שהחתימה (RS256)
    // אכן תואמת לתוכן האסימון, ובודק שהאסימון הונפק עבור פרויקט ה-Firebase שלנו (Firebase:ProjectId
    // ב-appsettings.json - אותו פרויקט המשמש גם לחיבור Firestore הרגיל) ושלא פג תוקפו. רק לאחר מכן
    // מתחברים (או נרשמים אוטומטית, ללא סיסמה, אם זו הפעם הראשונה שהמייל הזה מתחבר).
    public async Task<User> LoginWithGoogleAsync(GoogleLoginDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.IdToken))
            throw new ArgumentException("חסר אסימון התחברות");

        if (string.IsNullOrWhiteSpace(_firebaseAuthOptions.ProjectId))
        {
            throw new InvalidOperationException(
                "התחברות עם Google לא הוגדרה בצד השרת (Firebase:ProjectId חסר ב-appsettings.json).");
        }

        var parts = dto.IdToken.Split('.');
        if (parts.Length != 3)
            throw new UnauthorizedAccessException("אסימון ההתחברות אינו תקין");

        FirebaseTokenHeader header;
        FirebaseTokenPayload payload;
        try
        {
            header = JsonSerializer.Deserialize<FirebaseTokenHeader>(Base64UrlDecode(parts[0]))
                ?? throw new FormatException();
            payload = JsonSerializer.Deserialize<FirebaseTokenPayload>(Base64UrlDecode(parts[1]))
                ?? throw new FormatException();
        }
        catch (FormatException)
        {
            throw new UnauthorizedAccessException("אסימון ההתחברות אינו תקין");
        }

        var cert = await GetSigningCertAsync(header.Kid);
        if (cert is null)
            throw new UnauthorizedAccessException("אסימון ההתחברות נחתם במפתח לא מוכר");

        var signedData = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var signature = Base64UrlDecode(parts[2]);

        using var rsa = cert.GetRSAPublicKey()
            ?? throw new UnauthorizedAccessException("לא ניתן לאמת את חתימת האסימון");

        if (!rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new UnauthorizedAccessException("חתימת אסימון ההתחברות אינה תקינה");

        var expectedIssuer = $"https://securetoken.google.com/{_firebaseAuthOptions.ProjectId}";
        if (payload.Iss != expectedIssuer || payload.Aud != _firebaseAuthOptions.ProjectId)
            throw new UnauthorizedAccessException("אסימון ההתחברות אינו שייך לאתר זה");

        if (DateTimeOffset.FromUnixTimeSeconds(payload.Exp) < DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("אסימון ההתחברות פג תוקף");

        if (!payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
            throw new UnauthorizedAccessException("כתובת המייל של חשבון ה-Google אינה מאומתת");

        var user = await _userRepository.GetByEmailAsync(payload.Email);
        if (user is not null)
        {
            await RecordLoginAsync(user);
            return user;
        }

        // הרשמה אוטומטית בפעם הראשונה - ללא סיסמה (PasswordHash נשאר null; אפשר להתחבר עם
        // חשבון זה רק דרך Google, אלא אם המשתמש יגדיר סיסמה בהמשך מהפרופיל שלו).
        var newUser = new User
        {
            Name = payload.Name ?? payload.Email,
            Email = payload.Email.Trim().ToLowerInvariant(),
            Phone = string.Empty,
            Role = UserRole.Owner,
            CreatedAtUtc = DateTime.UtcNow,
            LastLoginUtc = DateTime.UtcNow,
            TermsAcceptedAtUtc = DateTime.UtcNow,
            TermsVersion = TermsInfo.CurrentVersion,
            PasswordHash = null
        };

        var createdUser = await _userRepository.CreateAsync(newUser);
        await SendWelcomeEmailAsync(createdUser);
        return createdUser;
    }

    private static string PhoneKey(string? phone)
    {
        var digits = new string((phone ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits.Length >= 9 ? digits[^9..] : digits;
    }

    // "ab***@gmail.com"
    private static string MaskEmail(string? email)
    {
        var at = (email ?? string.Empty).IndexOf('@');
        return at <= 0 ? "***" : $"{email![..Math.Min(2, at)]}***{email[at..]}";
    }

    private async Task<X509Certificate2?> GetSigningCertAsync(string kid)
    {
        lock (CertsCacheLock)
        {
            if (_cachedCerts is not null && _cachedCerts.TryGetValue(kid, out var cachedPem) &&
                DateTime.UtcNow - _certsCachedAtUtc < TimeSpan.FromHours(1))
            {
                return X509Certificate2.CreateFromPem(cachedPem);
            }
        }

        var fresh = await _http.GetFromJsonAsync<Dictionary<string, string>>(FirebaseCertsUrl)
            ?? throw new UnauthorizedAccessException("אימות מול Google נכשל");

        lock (CertsCacheLock)
        {
            _cachedCerts = fresh;
            _certsCachedAtUtc = DateTime.UtcNow;
        }

        return fresh.TryGetValue(kid, out var pem) ? X509Certificate2.CreateFromPem(pem) : null;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    private class FirebaseTokenHeader
    {
        [JsonPropertyName("kid")]
        public string Kid { get; set; } = string.Empty;
    }

    private class FirebaseTokenPayload
    {
        [JsonPropertyName("iss")]
        public string Iss { get; set; } = string.Empty;

        [JsonPropertyName("aud")]
        public string Aud { get; set; } = string.Empty;

        [JsonPropertyName("exp")]
        public long Exp { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("email_verified")]
        public bool EmailVerified { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // תוקף אסימון איפוס הסיסמה - חלון זמן קצר וסביר לשימוש חד-פעמי
    private static readonly TimeSpan PasswordResetTokenLifetime = TimeSpan.FromMinutes(30);

    public async Task<ForgotPasswordResponseDto> RequestPasswordResetAsync(ForgotPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ArgumentException("יש להזין כתובת אימייל");

        var user = await _userRepository.GetByEmailAsync(dto.Email);

        // גם אם לא נמצא משתמש עם המייל הזה, מחזירים תשובת "הצלחה" גנרית זהה (בלי אסימון) -
        // כדי לא לחשוף לתוקף פוטנציאלי אילו כתובות מייל רשומות במערכת ואילו לא.
        if (user is null)
        {
            return new ForgotPasswordResponseDto
            {
                Message = "אם קיים חשבון עם כתובת המייל הזו, הקישור לאיפוס הסיסמה הופק בהצלחה.",
                ResetToken = null
            };
        }

        var token = Guid.NewGuid().ToString("N");
        var expiresUtc = DateTime.UtcNow.Add(PasswordResetTokenLifetime);
        await _userRepository.SetPasswordResetTokenAsync(user.Id!, token, expiresUtc);

        // אם מוגדר חיבור אימייל אמיתי (Resend) - שולחים את הקישור במייל אמיתי ולא מחזירים אותו
        // בתשובת ה-API (כדי לא לחשוף את האסימון לכל מי שקורא ל-endpoint הזה). אחרת (מצב הדגמה,
        // ללא שירות דוא"ל מוגדר) ממשיכים להחזיר את האסימון ישירות בתשובה כדי שהתכונה תישאר שמישה.
        if (_emailService.IsEnabled)
        {
            var resetLink = $"{_clientOptions.BaseUrl.TrimEnd('/')}/reset-password?token={token}";
            var html = $"""
                <p>שלום {System.Net.WebUtility.HtmlEncode(NameText.FirstName(user.Name))},</p>
                <p>קיבלנו בקשה לאיפוס הסיסמה שלכם ב-common. לחצו על הקישור הבא כדי להגדיר סיסמה חדשה
                (בתוקף ל-30 דקות):</p>
                <p><a href="{resetLink}">{resetLink}</a></p>
                <p>אם לא ביקשתם זאת, אפשר להתעלם מהודעה זו.</p>
                """;

            await _emailService.SendAsync(user.Email, user.Name, "איפוס סיסמה - common", html);

            return new ForgotPasswordResponseDto
            {
                Message = "אם קיים חשבון עם כתובת המייל הזו, נשלח אליו קישור לאיפוס הסיסמה.",
                ResetToken = null
            };
        }

        return new ForgotPasswordResponseDto
        {
            Message = "אם קיים חשבון עם כתובת המייל הזו, הקישור לאיפוס הסיסמה הופק בהצלחה.",
            ResetToken = token
        };
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token))
            throw new ArgumentException("קישור איפוס הסיסמה אינו תקין");
        RequireStrongPassword(dto.NewPassword);

        var user = await _userRepository.GetByPasswordResetTokenAsync(dto.Token);

        if (user is null || user.PasswordResetTokenExpiresUtc is null || user.PasswordResetTokenExpiresUtc < DateTime.UtcNow)
            throw new ArgumentException("קישור איפוס הסיסמה אינו תקין או שפג תוקפו - יש לבקש קישור חדש");

        await _userRepository.UpdatePasswordAsync(user.Id!, PasswordHasher.Hash(dto.NewPassword));
        await _userRepository.SetPasswordResetTokenAsync(user.Id!, null, null);
    }
}
