namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Security;

// מימוש שכבת הגישה לנתונים עבור משתמשים, מול Firestore REST API
public class UserRepository : IUserRepository
{
    private const string CollectionName = "Users";
    private readonly FirebaseDbContext _firebase;
    private readonly FieldEncryptor _encryptor;

    public UserRepository(FirebaseDbContext firebase, FieldEncryptor encryptor)
    {
        _firebase = firebase;
        _encryptor = encryptor;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync()
    {
        var documents = await _firebase.ListDocumentsAsync(CollectionName);
        return documents.Select(ToEntity).ToList();
    }

    public async Task<User?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id, ct);
        return document is null ? null : ToEntity(document);
    }

    // מזהים משתמשת לפי מייל בלי תלות באותיות גדולות/רווחים - כך אותו חשבון משמש גם להתחברות בסיסמה וגם דרך Google
    public async Task<User?> GetByEmailAsync(string email)
    {
        var trimmed = (email ?? string.Empty).Trim();
        if (trimmed.Length == 0)
            return null;
        var normalized = trimmed.ToLowerInvariant();

        foreach (var candidate in normalized == trimmed ? new[] { normalized } : new[] { normalized, trimmed })
        {
            var documents = await _firebase.RunQueryAsync(
                CollectionName,
                new (string, JsonNode)[] { (nameof(User.Email), FirestoreJson.StringField(candidate)) },
                limit: 1);
            if (documents.Count > 0)
                return ToEntity(documents[0]);
        }

        // חשבונות ישנים שנשמרו עם אותיות גדולות
        return (await GetAllAsync()).FirstOrDefault(u => string.Equals(u.Email?.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<User?> GetByPasswordResetTokenAsync(string token)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(User.PasswordResetToken), FirestoreJson.StringField(token)) },
            limit: 1);

        return documents.Count > 0 ? ToEntity(documents[0]) : null;
    }

    public async Task<User> CreateAsync(User user)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(user));
        user.Id = FirestoreJson.ExtractDocumentId(created);
        return user;
    }

    public async Task UpdateAddressAsync(string id, string address, string city, string? addressNotes, string? floor, string? apartment, string? street = null)
    {
        var fields = new JsonObject
        {
            [nameof(User.Address)] = FirestoreJson.StringField(address),
            [nameof(User.City)] = FirestoreJson.StringField(city),
            [nameof(User.AddressNotes)] = FirestoreJson.StringOrNullField(addressNotes),
            [nameof(User.AddressFloor)] = FirestoreJson.StringOrNullField(floor),
            [nameof(User.AddressApartment)] = FirestoreJson.StringOrNullField(apartment),
            [nameof(User.AddressStreet)] = FirestoreJson.StringOrNullField(street)
        };

        await _firebase.PatchDocumentFieldsAsync(
            CollectionName,
            id,
            fields,
            new[] { nameof(User.Address), nameof(User.City), nameof(User.AddressNotes), nameof(User.AddressFloor), nameof(User.AddressApartment), nameof(User.AddressStreet) });
    }

    public async Task UpdateCommissionConsentAsync(string id, DateTime consentAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(User.CommissionConsentAtUtc)] = FirestoreJson.TimestampField(consentAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(User.CommissionConsentAtUtc) });
    }

    public async Task UpdateSaleCommissionConsentAsync(string id, DateTime consentAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(User.SaleCommissionConsentAtUtc)] = FirestoreJson.TimestampField(consentAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(User.SaleCommissionConsentAtUtc) });
    }

    public async Task UpdateShippingCardAsync(string id, DateTime consentAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(User.ShippingCardConsentAtUtc)] = FirestoreJson.TimestampField(consentAtUtc),
            [nameof(User.ShippingCardSaved)] = FirestoreJson.BooleanField(true)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(User.ShippingCardConsentAtUtc), nameof(User.ShippingCardSaved) });
    }

    public async Task UpdateAutoChargeAsync(string id, DateTime? consentAtUtc, bool enabled)
    {
        var fields = new JsonObject
        {
            [nameof(User.AutoChargeConsentAtUtc)] = FirestoreJson.TimestampOrNullField(consentAtUtc),
            [nameof(User.AutoChargeEnabled)] = FirestoreJson.BooleanField(enabled)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(User.AutoChargeConsentAtUtc), nameof(User.AutoChargeEnabled) });
    }

    public async Task UpdateLastLoginAsync(string id, DateTime loginAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(User.LastLoginUtc)] = FirestoreJson.TimestampField(loginAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(User.LastLoginUtc) });
    }

    public async Task UpdateQuietHoursAsync(string id, bool enabled, string? from, string? to)
    {
        var fields = new JsonObject
        {
            [nameof(User.QuietHoursEnabled)] = FirestoreJson.BooleanField(enabled),
            [nameof(User.QuietFrom)] = FirestoreJson.StringOrNullField(from),
            [nameof(User.QuietTo)] = FirestoreJson.StringOrNullField(to)
        };

        await _firebase.PatchDocumentFieldsAsync(
            CollectionName,
            id,
            fields,
            new[] { nameof(User.QuietHoursEnabled), nameof(User.QuietFrom), nameof(User.QuietTo) });
    }

    public async Task UpdateProfileAsync(string id, string name, string phone)
    {
        var fields = new JsonObject
        {
            [nameof(User.Name)] = FirestoreJson.StringField(name),
            [nameof(User.Phone)] = FirestoreJson.StringField(phone)
        };

        await _firebase.PatchDocumentFieldsAsync(
            CollectionName,
            id,
            fields,
            new[] { nameof(User.Name), nameof(User.Phone) });
    }

    public async Task UpdatePayoutAsync(
        string id,
        string accountHolderName,
        string bankName,
        string branchNumber,
        string accountNumber,
        DateTime consentAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(User.PayoutAccountHolderName)] = FirestoreJson.StringField(accountHolderName),
            [nameof(User.PayoutBankName)] = FirestoreJson.StringField(bankName),
            [nameof(User.PayoutBranchNumber)] = FirestoreJson.StringField(branchNumber),
            [nameof(User.PayoutAccountNumber)] = FirestoreJson.StringField(_encryptor.Encrypt(accountNumber)!),
            [nameof(User.PayoutConsentAtUtc)] = FirestoreJson.TimestampField(consentAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(
            CollectionName,
            id,
            fields,
            new[]
            {
                nameof(User.PayoutAccountHolderName),
                nameof(User.PayoutBankName),
                nameof(User.PayoutBranchNumber),
                nameof(User.PayoutAccountNumber),
                nameof(User.PayoutConsentAtUtc)
            });
    }

    public async Task<int> EncryptLegacyPayoutAccountNumbersAsync()
    {
        if (!_encryptor.IsEnabled)
        {
            return 0;
        }

        // קוראים את המסמכים הגולמיים (ולא דרך ToEntity שמפענח) כדי לזהות ערכים שעדיין לא מוצפנים
        var documents = await _firebase.ListDocumentsAsync(CollectionName);
        var updated = 0;

        foreach (var document in documents)
        {
            var id = FirestoreJson.ExtractDocumentId(document);
            var stored = FirestoreJson.GetStringOrNull(document["fields"] as JsonObject, nameof(User.PayoutAccountNumber));
            if (id is null || string.IsNullOrEmpty(stored) || FieldEncryptor.IsEncrypted(stored))
            {
                continue;
            }

            var fields = new JsonObject
            {
                [nameof(User.PayoutAccountNumber)] = FirestoreJson.StringField(_encryptor.Encrypt(stored)!)
            };
            await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(User.PayoutAccountNumber) });
            updated++;
        }

        return updated;
    }

    public async Task SetPasswordResetTokenAsync(string id, string? token, DateTime? expiresUtc)
    {
        var fields = new JsonObject
        {
            [nameof(User.PasswordResetToken)] = FirestoreJson.StringOrNullField(token),
            [nameof(User.PasswordResetTokenExpiresUtc)] = FirestoreJson.TimestampOrNullField(expiresUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(
            CollectionName,
            id,
            fields,
            new[] { nameof(User.PasswordResetToken), nameof(User.PasswordResetTokenExpiresUtc) });
    }

    public async Task UpdatePasswordAsync(string id, string passwordHash)
    {
        var fields = new JsonObject
        {
            [nameof(User.PasswordHash)] = FirestoreJson.StringField(passwordHash)
        };

        await _firebase.PatchDocumentFieldsAsync(
            CollectionName,
            id,
            fields,
            new[] { nameof(User.PasswordHash) });
    }

    public async Task DeleteAsync(string id)
    {
        await _firebase.DeleteDocumentAsync(CollectionName, id);
    }

    private JsonObject ToFields(User user)
    {
        return new JsonObject
        {
            [nameof(User.Name)] = FirestoreJson.StringField(user.Name),
            [nameof(User.Email)] = FirestoreJson.StringField(user.Email),
            [nameof(User.Phone)] = FirestoreJson.StringField(user.Phone),
            [nameof(User.Role)] = FirestoreJson.StringField(user.Role.ToString()),
            [nameof(User.PasswordHash)] = FirestoreJson.StringOrNullField(user.PasswordHash),
            [nameof(User.CreatedAtUtc)] = FirestoreJson.TimestampOrNullField(user.CreatedAtUtc),
            [nameof(User.LastLoginUtc)] = FirestoreJson.TimestampOrNullField(user.LastLoginUtc),
            [nameof(User.CommissionConsentAtUtc)] = FirestoreJson.TimestampOrNullField(user.CommissionConsentAtUtc),
            [nameof(User.SaleCommissionConsentAtUtc)] = FirestoreJson.TimestampOrNullField(user.SaleCommissionConsentAtUtc),
            [nameof(User.TermsAcceptedAtUtc)] = FirestoreJson.TimestampOrNullField(user.TermsAcceptedAtUtc),
            [nameof(User.TermsVersion)] = FirestoreJson.StringOrNullField(user.TermsVersion),
            [nameof(User.AutoChargeConsentAtUtc)] = FirestoreJson.TimestampOrNullField(user.AutoChargeConsentAtUtc),
            [nameof(User.AutoChargeEnabled)] = FirestoreJson.BooleanField(user.AutoChargeEnabled),
            [nameof(User.ShippingCardSaved)] = FirestoreJson.BooleanField(user.ShippingCardSaved),
            [nameof(User.QuietHoursEnabled)] = FirestoreJson.BooleanField(user.QuietHoursEnabled),
            [nameof(User.QuietFrom)] = FirestoreJson.StringOrNullField(user.QuietFrom),
            [nameof(User.QuietTo)] = FirestoreJson.StringOrNullField(user.QuietTo),
            [nameof(User.ShippingCardConsentAtUtc)] = FirestoreJson.TimestampOrNullField(user.ShippingCardConsentAtUtc),
            [nameof(User.Address)] = FirestoreJson.StringOrNullField(user.Address),
            [nameof(User.City)] = FirestoreJson.StringOrNullField(user.City),
            [nameof(User.AddressNotes)] = FirestoreJson.StringOrNullField(user.AddressNotes),
            [nameof(User.AddressFloor)] = FirestoreJson.StringOrNullField(user.AddressFloor),
            [nameof(User.AddressApartment)] = FirestoreJson.StringOrNullField(user.AddressApartment),
            [nameof(User.AddressStreet)] = FirestoreJson.StringOrNullField(user.AddressStreet),
            [nameof(User.PayoutAccountHolderName)] = FirestoreJson.StringOrNullField(user.PayoutAccountHolderName),
            [nameof(User.PayoutBankName)] = FirestoreJson.StringOrNullField(user.PayoutBankName),
            [nameof(User.PayoutBranchNumber)] = FirestoreJson.StringOrNullField(user.PayoutBranchNumber),
            [nameof(User.PayoutAccountNumber)] = FirestoreJson.StringOrNullField(_encryptor.Encrypt(user.PayoutAccountNumber)),
            [nameof(User.PayoutConsentAtUtc)] = FirestoreJson.TimestampOrNullField(user.PayoutConsentAtUtc),
            [nameof(User.PasswordResetToken)] = FirestoreJson.StringOrNullField(user.PasswordResetToken),
            [nameof(User.PasswordResetTokenExpiresUtc)] = FirestoreJson.TimestampOrNullField(user.PasswordResetTokenExpiresUtc)
        };
    }

    private User ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;

        return new User
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            Name = FirestoreJson.GetString(fields, nameof(User.Name)),
            Email = FirestoreJson.GetString(fields, nameof(User.Email)),
            Phone = FirestoreJson.GetString(fields, nameof(User.Phone)),
            Role = FirestoreJson.GetEnum(fields, nameof(User.Role), UserRole.Renter),
            PasswordHash = FirestoreJson.GetStringOrNull(fields, nameof(User.PasswordHash)),
            CreatedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.CreatedAtUtc)),
            LastLoginUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.LastLoginUtc)),
            CommissionConsentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.CommissionConsentAtUtc)),
            SaleCommissionConsentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.SaleCommissionConsentAtUtc)),
            TermsAcceptedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.TermsAcceptedAtUtc)),
            TermsVersion = FirestoreJson.GetStringOrNull(fields, nameof(User.TermsVersion)),
            AutoChargeConsentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.AutoChargeConsentAtUtc)),
            AutoChargeEnabled = FirestoreJson.GetBool(fields, nameof(User.AutoChargeEnabled)),
            ShippingCardSaved = FirestoreJson.GetBool(fields, nameof(User.ShippingCardSaved)),
            QuietHoursEnabled = FirestoreJson.GetBool(fields, nameof(User.QuietHoursEnabled)),
            QuietFrom = FirestoreJson.GetStringOrNull(fields, nameof(User.QuietFrom)),
            QuietTo = FirestoreJson.GetStringOrNull(fields, nameof(User.QuietTo)),
            ShippingCardConsentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.ShippingCardConsentAtUtc)),
            Address = FirestoreJson.GetStringOrNull(fields, nameof(User.Address)),
            City = FirestoreJson.GetStringOrNull(fields, nameof(User.City)),
            AddressNotes = FirestoreJson.GetStringOrNull(fields, nameof(User.AddressNotes)),
            AddressFloor = FirestoreJson.GetStringOrNull(fields, nameof(User.AddressFloor)),
            AddressApartment = FirestoreJson.GetStringOrNull(fields, nameof(User.AddressApartment)),
            AddressStreet = FirestoreJson.GetStringOrNull(fields, nameof(User.AddressStreet)),
            PayoutAccountHolderName = FirestoreJson.GetStringOrNull(fields, nameof(User.PayoutAccountHolderName)),
            PayoutBankName = FirestoreJson.GetStringOrNull(fields, nameof(User.PayoutBankName)),
            PayoutBranchNumber = FirestoreJson.GetStringOrNull(fields, nameof(User.PayoutBranchNumber)),
            PayoutAccountNumber = _encryptor.Decrypt(FirestoreJson.GetStringOrNull(fields, nameof(User.PayoutAccountNumber))),
            PayoutConsentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.PayoutConsentAtUtc)),
            PasswordResetToken = FirestoreJson.GetStringOrNull(fields, nameof(User.PasswordResetToken)),
            PasswordResetTokenExpiresUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(User.PasswordResetTokenExpiresUtc))
        };
    }
}
