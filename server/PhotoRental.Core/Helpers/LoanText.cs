namespace PhotoRental.Core.Helpers;

using PhotoRental.Core.Entities;

public static class LoanText
{
    // שם הפריט בהודעות על השכרה - עם הכמות כשהושכרה יותר מיחידה אחת
    public static string Name(Item? item, Loan loan, string fallback = "פריט") =>
        WithUnits(item?.Name ?? fallback, loan.Units);

    public static string WithUnits(string name, int units) =>
        units > 1 ? $"{name} ({units} יחידות)" : name;
}
