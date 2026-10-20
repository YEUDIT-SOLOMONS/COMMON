using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Interfaces;

namespace PhotoRental.API.Controllers;

// "יש לי שאלה למשכירה": שאלה של שוכרת על פריט, והתכתבות בקישור אישי מהמייל. הנהלת האתר רואה הכל
[ApiController]
[Route("api/questions")]
public class QuestionsController : ControllerBase
{
    private readonly IQuestionService _questions;

    public QuestionsController(IQuestionService questions)
    {
        _questions = questions;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    public class TextDto
    {
        public string? ItemId { get; set; }
        public string Text { get; set; } = string.Empty;
        public List<string>? Images { get; set; }
    }

    public class AdminMessageDto
    {
        public string Text { get; set; } = string.Empty;
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] TextDto dto)
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized();
        return Ok(await _questions.AskAsync(UserId, dto.ItemId, dto.Text, dto.Images));
    }

    // ההתכתבויות שלי (כשוכרת וכמשכירה), החדשות ראשונות - לאזור האישי
    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        if (string.IsNullOrEmpty(UserId))
            return Unauthorized();
        return Ok(await _questions.MineAsync(UserId));
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get([FromRoute] string id, [FromQuery] string? t)
    {
        var thread = await _questions.GetAsync(id, t, UserId);
        return thread is null ? NotFound(new { Message = "ההתכתבות לא נמצאה" }) : Ok(thread);
    }

    // הודעת מערכת מהנהלת האתר בתוך ההתכתבות - נשלח מייל לשתיהן
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/admin-messages")]
    public async Task<IActionResult> AdminMessage([FromRoute] string id, [FromBody] AdminMessageDto dto)
    {
        await _questions.AddAdminMessageAsync(id, dto.Text);
        return NoContent();
    }

    // תמונה מההתכתבות - טעינת תמונה רגילה עם הקישור האישי (כמו תמונה במייל), לא מקישור ציבורי
    [AllowAnonymous]
    [HttpGet("{id}/images/{imageId}")]
    public async Task<IActionResult> Image([FromRoute] string id, [FromRoute] string imageId, [FromQuery] string? t)
    {
        var image = await _questions.GetImageAsync(id, imageId, t, UserId);
        if (image is null)
            return NotFound();
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(image.Bytes, image.Mime);
    }

    [AllowAnonymous]
    [HttpPost("{id}/messages")]
    public async Task<IActionResult> Reply([FromRoute] string id, [FromQuery] string? t, [FromBody] TextDto dto)
    {
        await _questions.ReplyAsync(id, t, UserId, dto.Text, dto.Images);
        return NoContent();
    }
}
