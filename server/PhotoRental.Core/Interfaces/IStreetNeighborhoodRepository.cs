namespace PhotoRental.Core.Interfaces;

// שיוך רחוב לשכונה (נקבע בלשונית "שכונות" בניהול). מפתח: "עיר|רחוב"
public interface IStreetNeighborhoodRepository
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync();
    Task SetAsync(string city, string street, string? neighborhood);
}
