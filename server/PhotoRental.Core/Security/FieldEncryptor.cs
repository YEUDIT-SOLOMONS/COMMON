namespace PhotoRental.Core.Security;

using System.Security.Cryptography;
using System.Text;

// הצפנת שדות רגישים לפני שמירה במסד הנתונים (כרגע: מספר חשבון הבנק של המשכיר) - AES-256-GCM,
// מובנה ב-.NET ואינו דורש חבילת NuGet חיצונית. GCM מספק גם סודיות וגם אימות שלמות, כך שערך
// שנערך ידנית במסד הנתונים ייכשל בפענוח במקום להחזיר נתון שגוי בשקט.
//
// פורמט הערך השמור: "enc:v1:{Base64 של nonce(12) + ciphertext + tag(16)}". ערך בלי הקידומת
// נחשב טקסט גלוי ישן (נשמר לפני שההצפנה הופעלה) ומוחזר כמו שהוא - כך אפשר להפעיל את ההצפנה
// בלי לשבור משתמשים קיימים, והערכים הישנים מוצפנים ברקע (ר' PayoutEncryptionMigrationService).
//
// המפתח: 32 בתים ב-Base64 מההגדרה Encryption:PayoutKey (משתנה סביבה Encryption__PayoutKey
// ב-Cloud Run). אם לא הוגדר מפתח - IsEnabled=false והערכים נשמרים כמו קודם (עם אזהרה ביומן
// בעליית השרת), כדי שהאתר לא ייפול עד שהמפתח יוגדר. לעולם אין לשנות/לאבד את המפתח אחרי שיש
// נתונים מוצפנים - בלעדיו אי אפשר לפענח אותם.
public sealed class FieldEncryptor
{
    public const string Prefix = "enc:v1:";
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    private readonly byte[]? _key;

    public FieldEncryptor(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return;
        }

        var key = Convert.FromBase64String(base64Key.Trim());
        if (key.Length != 32)
        {
            throw new InvalidOperationException("Encryption:PayoutKey חייב להיות 32 בתים (256 ביט) בקידוד Base64.");
        }

        _key = key;
    }

    public bool IsEnabled => _key is not null;

    public static bool IsEncrypted(string? value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    // מצפין ערך לשמירה. null/ריק נשארים כמו שהם; ערך שכבר מוצפן לא מוצפן פעמיים.
    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext) || _key is null || IsEncrypted(plaintext))
        {
            return plaintext;
        }

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(_key, TagSizeBytes))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        var payload = new byte[NonceSizeBytes + cipher.Length + TagSizeBytes];
        nonce.CopyTo(payload, 0);
        cipher.CopyTo(payload, NonceSizeBytes);
        tag.CopyTo(payload, NonceSizeBytes + cipher.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    // מפענח ערך שנקרא מהמסד. ערך ללא הקידומת (טקסט גלוי ישן) מוחזר כמו שהוא.
    public string? Decrypt(string? stored)
    {
        if (!IsEncrypted(stored))
        {
            return stored;
        }

        if (_key is null)
        {
            throw new InvalidOperationException("נמצא ערך מוצפן במסד הנתונים אך לא הוגדר Encryption:PayoutKey - אי אפשר לפענח.");
        }

        var payload = Convert.FromBase64String(stored!.Substring(Prefix.Length));
        var cipherLength = payload.Length - NonceSizeBytes - TagSizeBytes;
        if (cipherLength < 0)
        {
            throw new CryptographicException("ערך מוצפן פגום.");
        }

        var nonce = payload.AsSpan(0, NonceSizeBytes);
        var cipher = payload.AsSpan(NonceSizeBytes, cipherLength);
        var tag = payload.AsSpan(NonceSizeBytes + cipherLength, TagSizeBytes);
        var plainBytes = new byte[cipherLength];

        using (var aes = new AesGcm(_key, TagSizeBytes))
        {
            aes.Decrypt(nonce, cipher, tag, plainBytes);
        }

        return Encoding.UTF8.GetString(plainBytes);
    }
}
