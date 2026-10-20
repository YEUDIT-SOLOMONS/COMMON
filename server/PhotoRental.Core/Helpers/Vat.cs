namespace PhotoRental.Core.Helpers;

// מע"מ: פריט שהמשכירה סימנה "הוסף מע"מ" נשמר עם המחיר הכולל, כך שכל החישובים משתמשים במחיר הסופי
public static class Vat
{
    public const decimal Rate = 0.18m;

    public static decimal Add(decimal priceBeforeVat) => Math.Round(priceBeforeVat * (1 + Rate), 2, MidpointRounding.AwayFromZero);
}
