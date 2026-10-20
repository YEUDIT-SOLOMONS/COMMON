namespace PhotoRental.Core.Options;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "PhotoRentalAPI";
    public string Audience { get; set; } = "PhotoRentalClient";
    public int ExpiryMinutes { get; set; } = 1440;
}
