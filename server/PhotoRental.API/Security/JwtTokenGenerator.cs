namespace PhotoRental.API.Security;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Options;

// הנפקת טוקן JWT חתום עבור משתמש מאומת. שייכת לשכבת ה-API (ולא לשכבת השירות),
// כי הנפקת/אימות טוקנים היא אחריות של שכבת האירוח (Web Host) בלבד, ותלויה בהגדרות
// התצורה (Jwt:Key/Issuer/Audience) שנטענות כאן ובאותה צורה ב-Program.cs (לאימות הטוקן בחזרה).
public class JwtTokenGenerator : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGenerator(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public (string Token, DateTime ExpiresAtUtc) GenerateToken(User user)
    {
        var key = _options.Key;
        var issuer = _options.Issuer;
        var audience = _options.Audience;
        var expiryMinutes = _options.ExpiryMinutes;

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
