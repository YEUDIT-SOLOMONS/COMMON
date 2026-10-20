namespace PhotoRental.API.Security;

using PhotoRental.Core.Entities;

// אחראית להנפקת טוקן JWT חתום עבור משתמש מאומת
public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAtUtc) GenerateToken(User user);
}
