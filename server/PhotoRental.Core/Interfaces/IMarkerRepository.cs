namespace PhotoRental.Core.Interfaces;

// סימון חד-פעמי ("כבר נשלח") לתזכורות ובקשות אוטומטיות, כדי שלא יישלחו פעמיים
public interface IMarkerRepository
{
    Task<bool> ExistsAsync(string key);
    Task MarkAsync(string key);
}
