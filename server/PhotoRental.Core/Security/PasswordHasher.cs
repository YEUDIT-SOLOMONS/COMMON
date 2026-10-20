namespace PhotoRental.Core.Security;

using System.Security.Cryptography;

// גיבוב ואימות סיסמאות בשיטת PBKDF2 (Rfc2898DeriveBytes) - תקן מאובטח ומקובל, מובנה במסגרת
// העבודה (.NET) ואינו דורש הוספת חבילת NuGet חיצונית. הפורמט הנשמר במסד הנתונים:
// "{מספר איטרציות}.{מלח ב-Base64}.{גיבוב ב-Base64}"
public static class PasswordHasher
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Iterations = 100_000;

    // הצפנת סיסמה חדשה למחרוזת גיבוב לשמירה במסד הנתונים (לעולם לא נשמרת הסיסמה עצמה)
    public static string Hash(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException("סיסמה לא יכולה להיות ריקה", nameof(password));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSizeBytes);

        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    // בדיקה שהסיסמה שהוזנה בהתחברות תואמת לגיבוב השמור, ללא חשיפת הסיסמה המקורית בשום שלב
    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expectedHash;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            expectedHash = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);

        // השוואה בזמן קבוע (Constant-Time) כדי למנוע התקפות תזמון (Timing Attacks)
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
