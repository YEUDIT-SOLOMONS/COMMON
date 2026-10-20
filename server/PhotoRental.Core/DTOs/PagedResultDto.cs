namespace PhotoRental.Core.DTOs;

using System.ComponentModel.DataAnnotations;

public class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class ItemPageQueryDto
{
    [Range(1, 10_000)]
    public int Page { get; set; } = 1;

    [Range(1, 50)]
    public int PageSize { get; set; } = 12;

    public string? CategoryId { get; set; }

    [RegularExpression("^(newest|price|name)$", ErrorMessage = "מיון אפשרי: newest, price, name")]
    public string Sort { get; set; } = "newest";
}
