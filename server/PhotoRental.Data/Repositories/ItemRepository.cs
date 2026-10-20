namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Enums;
using PhotoRental.Core.Interfaces;

// מימוש שכבת הגישה לנתונים עבור פריטי ציוד, מול Firestore REST API
public class ItemRepository : IItemRepository
{
    private const string CollectionName = "Items";
    private readonly FirebaseDbContext _firebase;

    public ItemRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<IReadOnlyList<Item>> GetAllAsync()
    {
        var documents = await _firebase.ListDocumentsAsync(CollectionName);
        return documents.Select(ToEntity).ToList();
    }

    public async Task<(IReadOnlyList<Item> Items, int TotalCount)> GetPageAsync(ItemPageQueryDto query, CancellationToken ct = default)
    {
        var filters = new List<JsonObject>
        {
            FirebaseDbContext.IsNullFilter(nameof(Item.DeletedAtUtc)),
            FirebaseDbContext.IsNullFilter(nameof(Item.SoldAtUtc))
        };
        if (!string.IsNullOrWhiteSpace(query.CategoryId))
            filters.Add(FirebaseDbContext.EqualsFilter(nameof(Item.CategoryId), FirestoreJson.StringField(query.CategoryId)));

        (string, bool)[] orderBy = query.Sort switch
        {
            "price" => new[] { (nameof(Item.DailyPrice), false), ("__name__", false) },
            "name" => new[] { (nameof(Item.Name), false), ("__name__", false) },
            _ => new[] { (nameof(Item.CreatedAtUtc), true), ("__name__", true) }
        };

        var pageTask = _firebase.QueryPageAsync(CollectionName, filters, orderBy, (query.Page - 1) * query.PageSize, query.PageSize, ct);
        var countTask = _firebase.CountAsync(CollectionName, filters, ct);
        await Task.WhenAll(pageTask, countTask);

        return (pageTask.Result.Select(ToEntity).ToList(), countTask.Result);
    }

    public async Task<IReadOnlyDictionary<string, Item>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        var documents = await _firebase.BatchGetAsync(CollectionName, ids, ct);
        return documents.Select(ToEntity).Where(i => i.Id is not null).ToDictionary(i => i.Id!);
    }

    public async Task<Item?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id, ct);
        return document is null ? null : ToEntity(document);
    }

    public async Task<IReadOnlyList<Item>> GetByCategoryIdAsync(string categoryId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(Item.CategoryId), FirestoreJson.StringField(categoryId)) });

        return documents.Select(ToEntity).ToList();
    }

    public async Task<IReadOnlyList<Item>> GetByOwnerIdAsync(string ownerId)
    {
        var documents = await _firebase.RunQueryAsync(
            CollectionName,
            new (string, JsonNode)[] { (nameof(Item.OwnerId), FirestoreJson.StringField(ownerId)) });

        return documents.Select(ToEntity).ToList();
    }

    public async Task<Item> CreateAsync(Item item)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(item));
        item.Id = FirestoreJson.ExtractDocumentId(created);
        return item;
    }

    public async Task UpdateAsync(string id, Item item)
    {
        await _firebase.ReplaceDocumentAsync(CollectionName, id, ToFields(item));
    }

    public async Task DeleteAsync(string id)
    {
        await _firebase.DeleteDocumentAsync(CollectionName, id);
    }

    public async Task SoftDeleteAsync(string id, DateTime deletedAtUtc)
    {
        var fields = new JsonObject
        {
            [nameof(Item.DeletedAtUtc)] = FirestoreJson.TimestampField(deletedAtUtc)
        };

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Item.DeletedAtUtc) });
    }

    public async Task UpdateStatusAsync(string id, CopyStatus status, int? rentalCount = null)
    {
        var fields = new JsonObject { [nameof(Item.Status)] = FirestoreJson.StringField(status.ToString()) };
        var paths = new List<string> { nameof(Item.Status) };
        if (rentalCount.HasValue)
        {
            fields[nameof(Item.RentalCount)] = FirestoreJson.DoubleField(rentalCount.Value);
            paths.Add(nameof(Item.RentalCount));
        }

        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, paths);
    }

    public async Task PatchRentalCountAsync(string id, int rentalCount)
    {
        var fields = new JsonObject { [nameof(Item.RentalCount)] = FirestoreJson.DoubleField(rentalCount) };
        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, new[] { nameof(Item.RentalCount) });
    }

    public async Task PatchStaleStateAsync(string id, Item item)
    {
        var fields = StaleFields(item);
        await _firebase.PatchDocumentFieldsAsync(CollectionName, id, fields, fields.Select(pair => pair.Key).ToArray());
    }

    private static JsonObject StaleFields(Item item) => new()
    {
        [nameof(Item.CreatedAtUtc)] = FirestoreJson.TimestampOrNullField(item.CreatedAtUtc),
        [nameof(Item.AvailabilityConfirmedAtUtc)] = FirestoreJson.TimestampOrNullField(item.AvailabilityConfirmedAtUtc),
        [nameof(Item.StaleNoticeSentAtUtc)] = FirestoreJson.TimestampOrNullField(item.StaleNoticeSentAtUtc),
        [nameof(Item.StaleActionToken)] = FirestoreJson.StringOrNullField(item.StaleActionToken)
    };

    private static JsonObject ToFields(Item item)
    {
        return new JsonObject
        {
            [nameof(Item.Name)] = FirestoreJson.StringField(item.Name),
            [nameof(Item.Description)] = FirestoreJson.StringField(item.Description),
            [nameof(Item.DailyPrice)] = FirestoreJson.DoubleField((double)item.DailyPrice),
            [nameof(Item.HourlyPrice)] = FirestoreJson.DoubleOrNullField(item.HourlyPrice.HasValue ? (double)item.HourlyPrice.Value : null),
            [nameof(Item.VatAdded)] = FirestoreJson.BooleanField(item.VatAdded),
            [nameof(Item.Quantity)] = FirestoreJson.DoubleField(item.Quantity),
            [nameof(Item.IsLocation)] = FirestoreJson.BooleanField(item.IsLocation),
            [nameof(Item.ForSale)] = FirestoreJson.BooleanField(item.ForSale),
            [nameof(Item.SalePrice)] = FirestoreJson.DoubleField((double)item.SalePrice),
            [nameof(Item.SaleCondition)] = FirestoreJson.StringOrNullField(item.SaleCondition),
            [nameof(Item.SoldAtUtc)] = FirestoreJson.TimestampOrNullField(item.SoldAtUtc),
            [nameof(Item.ExtraCategoryId)] = FirestoreJson.StringOrNullField(item.ExtraCategoryId),
            [nameof(Item.MinRentalUnit)] = FirestoreJson.StringField(item.MinRentalUnit.ToString()),
            [nameof(Item.MinRentalAmount)] = FirestoreJson.DoubleField(item.MinRentalAmount),
            [nameof(Item.SerialNumber)] = FirestoreJson.StringField(item.SerialNumber),
            [nameof(Item.Status)] = FirestoreJson.StringField(item.Status.ToString()),
            [nameof(Item.CategoryId)] = FirestoreJson.StringOrNullField(item.CategoryId),
            [nameof(Item.OwnerId)] = FirestoreJson.StringOrNullField(item.OwnerId),
            [nameof(Item.ImageDataUrl)] = FirestoreJson.StringOrNullField(item.ImageDataUrl),
            ["ExtraImagesJson"] = FirestoreJson.StringOrNullField(
                item.ExtraImageDataUrls.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(item.ExtraImageDataUrls) : null),
            [nameof(Item.City)] = FirestoreJson.StringOrNullField(item.City),
            [nameof(Item.Address)] = FirestoreJson.StringOrNullField(item.Address),
            [nameof(Item.Street)] = FirestoreJson.StringOrNullField(item.Street),
            [nameof(Item.CreatedAtUtc)] = FirestoreJson.TimestampOrNullField(item.CreatedAtUtc),
            [nameof(Item.AvailabilityConfirmedAtUtc)] = FirestoreJson.TimestampOrNullField(item.AvailabilityConfirmedAtUtc),
            [nameof(Item.StaleNoticeSentAtUtc)] = FirestoreJson.TimestampOrNullField(item.StaleNoticeSentAtUtc),
            [nameof(Item.StaleActionToken)] = FirestoreJson.StringOrNullField(item.StaleActionToken),
            [nameof(Item.DeletedAtUtc)] = FirestoreJson.TimestampOrNullField(item.DeletedAtUtc),
            [nameof(Item.RentalCount)] = FirestoreJson.DoubleOrNullField(item.RentalCount),
            [nameof(Item.Version)] = FirestoreJson.IntegerField(item.Version)
        };
    }

    private static Item ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;

        return new Item
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            Version = FirestoreJson.GetLong(fields, nameof(Item.Version)),
            ConcurrencyToken = document["updateTime"]?.GetValue<string>(),
            Name = FirestoreJson.GetString(fields, nameof(Item.Name)),
            Description = FirestoreJson.GetString(fields, nameof(Item.Description)),
            DailyPrice = (decimal)FirestoreJson.GetDouble(fields, nameof(Item.DailyPrice)),
            HourlyPrice = FirestoreJson.GetDoubleOrNull(fields, nameof(Item.HourlyPrice)) is double hourly ? (decimal)hourly : null,
            VatAdded = FirestoreJson.GetBool(fields, nameof(Item.VatAdded)),
            Quantity = GetPositiveInt(fields, nameof(Item.Quantity), 1),
            IsLocation = FirestoreJson.GetBool(fields, nameof(Item.IsLocation)),
            ForSale = FirestoreJson.GetBool(fields, nameof(Item.ForSale)),
            SalePrice = (decimal)FirestoreJson.GetDouble(fields, nameof(Item.SalePrice)),
            SaleCondition = FirestoreJson.GetStringOrNull(fields, nameof(Item.SaleCondition)),
            SoldAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Item.SoldAtUtc)),
            ExtraCategoryId = FirestoreJson.GetStringOrNull(fields, nameof(Item.ExtraCategoryId)),
            MinRentalUnit = FirestoreJson.GetEnum(fields, nameof(Item.MinRentalUnit), RentalUnit.Days),
            MinRentalAmount = GetPositiveInt(fields, nameof(Item.MinRentalAmount), 1),
            SerialNumber = FirestoreJson.GetString(fields, nameof(Item.SerialNumber)),
            Status = FirestoreJson.GetEnum(fields, nameof(Item.Status), CopyStatus.Available),
            CategoryId = FirestoreJson.GetStringOrNull(fields, nameof(Item.CategoryId)),
            OwnerId = FirestoreJson.GetStringOrNull(fields, nameof(Item.OwnerId)),
            ImageDataUrl = FirestoreJson.GetStringOrNull(fields, nameof(Item.ImageDataUrl)),
            ExtraImageDataUrls = ParseExtraImages(FirestoreJson.GetStringOrNull(fields, "ExtraImagesJson")),
            City = FirestoreJson.GetStringOrNull(fields, nameof(Item.City)),
            Address = FirestoreJson.GetStringOrNull(fields, nameof(Item.Address)),
            Street = FirestoreJson.GetStringOrNull(fields, nameof(Item.Street)),
            // פריטים ישנים בלי השדה - מועד יצירת המסמך
            CreatedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Item.CreatedAtUtc))
                ?? (DateTime.TryParse(document["createTime"]?.ToString(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var created)
                    ? created
                    : null),
            AvailabilityConfirmedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Item.AvailabilityConfirmedAtUtc)),
            StaleNoticeSentAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Item.StaleNoticeSentAtUtc)),
            StaleActionToken = FirestoreJson.GetStringOrNull(fields, nameof(Item.StaleActionToken)),
            DeletedAtUtc = FirestoreJson.GetTimestampOrNull(fields, nameof(Item.DeletedAtUtc)),
            RentalCount = FirestoreJson.GetDoubleOrNull(fields, nameof(Item.RentalCount)) is double rentals ? (int)rentals : null
        };
    }

    private static List<string> ParseExtraImages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<string>();
        }
    }

    // מגן מפני ערך 0 או שלילי (למשל בפריטים ישנים שנוצרו לפני הוספת שדה זה) - חוזר לברירת המחדל.
    private static int GetPositiveInt(JsonObject? fields, string name, int defaultValue)
    {
        var value = (int)FirestoreJson.GetDouble(fields, name);
        return value > 0 ? value : defaultValue;
    }
}
