using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.DTOs;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    // שליפת כל הקטגוריות
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categories = await CatalogCache.GetOrAddAsync("categories", TimeSpan.FromSeconds(60), () => _categoryService.GetAllAsync());
        return Ok(categories);
    }

    // שליפת קטגוריה לפי ID
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById([FromRoute] string id)
    {
        var category = await _categoryService.GetByIdAsync(id);
        return category is null ? NotFound(new { Message = "הקטגוריה לא נמצאה" }) : Ok(category);
    }

    // ניהול קטגוריות (הוספה / עריכה / מחיקה) - למנהלות בלבד (תפקיד Admin). מסך הניהול באתר: /admin/categories.
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([FromBody] CategoryCreateDto dto)
    {
        var created = await _categoryService.CreateAsync(dto);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    // שינוי סדר ההצגה של הקטגוריות בקטלוג (מזהים לפי הסדר הרצוי)
    [HttpPut("order")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reorder([FromBody] CategoryReorderDto dto)
    {
        await _categoryService.ReorderAsync(dto);
        return NoContent();
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update([FromRoute] string id, [FromBody] CategoryCreateDto dto)
    {
        var updated = await _categoryService.UpdateAsync(id, dto);
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete([FromRoute] string id)
    {
        await _categoryService.DeleteAsync(id);
        return NoContent();
    }
}
