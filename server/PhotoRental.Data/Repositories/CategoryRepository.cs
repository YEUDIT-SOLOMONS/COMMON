namespace PhotoRental.Data.Repositories;

using System.Text.Json.Nodes;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// מימוש שכבת הגישה לנתונים עבור קטגוריות, מול Firestore REST API
public class CategoryRepository : ICategoryRepository
{
    private const string CollectionName = "Categories";
    private readonly FirebaseDbContext _firebase;

    public CategoryRepository(FirebaseDbContext firebase)
    {
        _firebase = firebase;
    }

    public async Task<IReadOnlyList<Category>> GetAllAsync()
    {
        var documents = await _firebase.ListDocumentsAsync(CollectionName);
        return documents.Select(ToEntity).ToList();
    }

    public async Task<Category?> GetByIdAsync(string id)
    {
        var document = await _firebase.GetDocumentAsync(CollectionName, id);
        return document is null ? null : ToEntity(document);
    }

    public async Task<Category> CreateAsync(Category category)
    {
        var created = await _firebase.CreateDocumentAsync(CollectionName, ToFields(category));
        category.Id = FirestoreJson.ExtractDocumentId(created);
        return category;
    }

    public async Task UpdateAsync(string id, Category category)
    {
        await _firebase.ReplaceDocumentAsync(CollectionName, id, ToFields(category));
    }

    public async Task DeleteAsync(string id)
    {
        await _firebase.DeleteDocumentAsync(CollectionName, id);
    }

    private static JsonObject ToFields(Category category)
    {
        return new JsonObject
        {
            [nameof(Category.Name)] = FirestoreJson.StringField(category.Name),
            [nameof(Category.Order)] = FirestoreJson.DoubleField(category.Order),
            [nameof(Category.ParentId)] = FirestoreJson.StringOrNullField(category.ParentId)
        };
    }

    private static Category ToEntity(JsonObject document)
    {
        var fields = document["fields"] as JsonObject;

        return new Category
        {
            Id = FirestoreJson.ExtractDocumentId(document),
            Name = FirestoreJson.GetString(fields, nameof(Category.Name)),
            Order = (int)FirestoreJson.GetDouble(fields, nameof(Category.Order)),
            ParentId = FirestoreJson.GetStringOrNull(fields, nameof(Category.ParentId))
        };
    }
}
