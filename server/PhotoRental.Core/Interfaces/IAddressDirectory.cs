namespace PhotoRental.Core.Interfaces;

// מדריך הכתובות הרשמי של ישראל (יישובים ורחובות) - לפי מאגרי המידע הממשלתיים ב-data.gov.il - להשלמה אוטומטית של כתובות,
// כך שכתובות נשמרות ונשלחות לחברת השילוח בשם התקני של היישוב והרחוב.
public interface IAddressDirectory
{
    // יישובים ששמם מתחיל ב-/מכיל את הטקסט (קודם "מתחיל ב"), עד limit תוצאות
    Task<IReadOnlyList<string>> SearchCitiesAsync(string query, int limit = 12);

    // רחובות של יישוב מסוים (השם המדויק, כפי שהוחזר מהשלמת היישובים) ששמם מתחיל ב-/מכיל את הטקסט
    Task<IReadOnlyList<string>> SearchStreetsAsync(string city, string query, int limit = 12);
}
