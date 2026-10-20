namespace PhotoRental.API.Security;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PhotoRental.Core.Entities;

// הנפקת טוקן JWT חתום עבור משתמש מאומת. שייכת לשכבת ה-API (ולא לשכבת השירות),
// כי הנפקת/אימות טוקנים היא אחריות של שכבת האירוח (Web Host) בלבד, ותלויה בהגדרות
// התצורה (Jwt:Key/Issuer/Audience) שנטענות כאן ובאותה צורה ב-Program.cs (לאימות הטוקן בחזרה).
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly IConfiguration _configuration;

    public JwtTokenGenerator(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(User user)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"]
            ?? throw new InvalidOperationException("יש להגדיר את Jwt:Key בקובץ ההגדרות appsettings.json");
        var issuer = jwtSection["Issuer"] ?? "PhotoRentalAPI";
        var audience = jwtSection["Audience"] ?? "PhotoRentalClient";
        var expiryMinutes = int.TryParse(jwtSection["ExpiryMinutes"], out var minutes) ? minutes : 1440;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id ?? string.Empty),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id ?? string.Empty),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
