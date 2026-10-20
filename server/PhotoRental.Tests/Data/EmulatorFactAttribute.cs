namespace PhotoRental.Tests.Data;

// בדיקה שרצה רק כש-Firestore Emulator פעיל (משתנה הסביבה FIRESTORE_EMULATOR_HOST מוגדר)
public sealed class EmulatorFactAttribute : FactAttribute
{
    public static string? Host => Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");

    public EmulatorFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Host))
            Skip = "Firestore Emulator לא פעיל. הפעילי: firebase emulators:start --only firestore";
    }
}
