using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Helpers;
using PhotoRental.Core.Interfaces;
using PhotoRental.Core.Options;

namespace PhotoRental.API.Controllers;

// "יש לי שאלה למשכירה": שאלה של שוכרת על פריט, והתכתבות בקישור אישי מהמייל. הנהלת האתר רואה הכל
[ApiController]
[Route("api/questions")]
public class QuestionsController : ControllerBase
{
    private const int MaxLength = 1000;
    private const int MaxMessages = 40;
    private const string Hidden = "[פרטי קשר הוסתרו]";
    private static readonly Regex Contact = new(
        @"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9.-]+)?|(?<!\d)(?:\+?\D{0,2}9\D{0,2}7\D{0,2}2|0)\D{0,3}[2-9](?:\D{0,3}\d){7,8}(?!\D{0,3}\d)|(?:(?:אפס|אחת|אחד|שתיים|שתים|שניים|שלוש|ארבע|חמש|שש|שבע|שמונה|תשע)[\s,.\-]*){7,}|wa\.me/\S+|chat\.whatsapp\.com/\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IItemQuestionRepository _questions;
    private readonly IItemRepository _items;
    private readonly IUserRepository _users;
    private readonly IEmailService _email;
    private readonly ClientOptions _client;

    public QuestionsController(IItemQuestionRepository questions, IItemRepository items, IUserRepository users, IEmailService email, ClientOptions client)
    {
        _questions = questions;
        _items = items;
        _users = users;
        _email = email;
        _client = client;
    }

    public class TextDto
    {
        public string? ItemId { get; set; }
        public string Text { get; set; } = string.Empty;
        public List<string>? Images { get; set; }
    }

    private const int MaxImages = 3;
    private const int MaxImageChars = 950_000;

    private static string? ImagesError(List<string>? images)
    {
        if (images is null || images.Count == 0)
            return null;
        if (images.Count > MaxImages)
            return $"אפשר לצרף עד {MaxImages} תמונות בהודעה";
        if (images.Any(i => !i.StartsWith("data:image/", StringComparison.Ordinal) || i.Length > MaxImageChars))
            return "אחת התמונות אינה תקינה או גדולה מדי";
        return null;
    }

    private async Task<List<string>> SaveImagesAsync(string threadId, List<string>? images)
    {
        var ids = new List<string>();
        foreach (var image in images ?? new List<string>())
            ids.Add(await _questions.AddImageAsync(threadId, image));
        return ids;
    }

    private static readonly Regex MailWord = new(@"מייל|אימייל|e-?mail|g-?mail|ג['׳]?י?מייל|שטרודל|שטרודעל|\bat\b|דוט|נקודה\s*קום|\.com|\bקום\b|walla|וואלה|yahoo|יאהו|hotmail|הוטמייל|outlook|icloud|instagram|אינסטגרם|אינסטה|facebook|פייסבוק|telegram|טלגרם|tiktok|טיקטוק", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AskedContact = new(@"מייל|e-?mail|טלפון|מספר|פלאפון|וואטסאפ|ווצאפ|אינסטגרם|אינסטה|instagram|פייסבוק|טלגרם", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LatinToken = new(@"(?<![A-Za-z0-9])[A-Za-z][A-Za-z0-9._-]{3,}", RegexOptions.Compiled);
    private static readonly Regex Brand = new(@"^(?:canon|sony|nikon|godox|fuji|fujifilm|sigma|tamron|profoto|rode|lumix|panasonic|olympus|leica|iphone|samsung|apple|manfrotto|aputure|elinchrom|broncolor|yongnuo|neewer|nanlite|smallrig|zhiyun|ronin|gopro|insta360|lightroom|photoshop|common|netfree|netsparkl|netspark)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // לשמירה על הקהילה: טלפון, מייל וקישורי וואטסאפ לא עוברים בהתכתבות.
    // גם שם משתמש של מייל בלי @ (ליד "שטרודל"/"גימייל", או בתשובה ל"מה המייל שלך?")
    public static string Clean(string text, string? previous = null)
    {
        var cleaned = Contact.Replace(text.Trim(), Hidden);
        var asked = previous is not null && AskedContact.IsMatch(previous);
        return LatinToken.Replace(cleaned, m =>
        {
            if (Brand.IsMatch(m.Value))
                return m.Value;
            var from = Math.Max(0, m.Index - 30);
            var near = cleaned.Substring(from, Math.Min(cleaned.Length, m.Index + m.Length + 30) - from).Replace(m.Value, " ");
            return MailWord.IsMatch(near) || (asked && m.Value.Length >= 5) ? Hidden : m.Value;
        });
    }

    private static string NewToken() =>
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] TextDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        // חשבון שנמחק (נשאר מחובר בדפדפן) - אין לאן לשלוח לה את התשובה
        if (await _users.GetByIdAsync(userId) is not { Email.Length: > 0 })
            return BadRequest(new { Message = "החשבון שלך לא נמצא - נא להתנתק ולהתחבר מחדש" });
        var text = (dto.Text ?? string.Empty).Trim();
        var hasImages = dto.Images is { Count: > 0 };
        if (text.Length < 2 && !hasImages)
            return BadRequest(new { Message = "יש לכתוב שאלה" });
        if (text.Length > MaxLength)
            return BadRequest(new { Message = "השאלה ארוכה מדי" });
        if (ImagesError(dto.Images) is { } imagesError)
            return BadRequest(new { Message = imagesError });

        var item = string.IsNullOrEmpty(dto.ItemId) ? null : await _items.GetByIdAsync(dto.ItemId);
        if (item is null || item.DeletedAtUtc is not null || string.IsNullOrEmpty(item.OwnerId))
            return NotFound(new { Message = "הפריט לא נמצא" });
        if (item.OwnerId == userId)
            return BadRequest(new { Message = "זה פריט שלך" });

        // שאלה נוספת על אותו פריט ממשיכה את אותה התכתבות
        var thread = (await _questions.GetAllAsync()).FirstOrDefault(q => q.ItemId == item.Id && q.RenterId == userId);
        var now = DateTime.UtcNow;
        if (thread is null)
        {
            thread = await _questions.CreateAsync(new ItemQuestion
            {
                ItemId = item.Id!,
                OwnerId = item.OwnerId,
                RenterId = userId,
                OwnerToken = NewToken(),
                RenterToken = NewToken(),
                CreatedAtUtc = now,
                LastMessageAtUtc = now
            });
            thread.Messages.Add(new ItemQuestionMessage
            {
                From = "Renter", Text = Clean(text), AtUtc = now, ImageIds = await SaveImagesAsync(thread.Id!, dto.Images)
            });
            await _questions.UpdateAsync(thread);
        }
        else
        {
            if (thread.Messages.Count >= MaxMessages)
                return BadRequest(new { Message = "ההתכתבות הזו הגיעה למקסימום הודעות" });
            thread.Messages.Add(new ItemQuestionMessage
            {
                From = "Renter", Text = Clean(text), AtUtc = now, ImageIds = await SaveImagesAsync(thread.Id!, dto.Images)
            });
            thread.LastMessageAtUtc = now;
            await _questions.UpdateAsync(thread);
        }

        await NotifyAsync(thread, item, toOwner: true, first: thread.Messages.Count == 1);
        return Ok(new { thread.Id, Token = thread.RenterToken });
    }

    // ההתכתבויות שלי (כשוכרת וכמשכירה), החדשות ראשונות - לאזור האישי
    [Authorize]
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();
        var mine = (await _questions.GetAllAsync())
            .Where(q => q.RenterId == userId || q.OwnerId == userId)
            .OrderByDescending(q => q.LastMessageAtUtc)
            .ToList();
        var rows = new List<object>();
        foreach (var q in mine)
        {
            var item = await _items.GetByIdAsync(q.ItemId);
            var role = q.OwnerId == userId ? "Owner" : "Renter";
            rows.Add(new
            {
                q.Id,
                Role = role,
                ItemName = item?.Name ?? "(פריט שנמחק)",
                Image = item is null ? null : ImageLinks.Thumb(item.Id, item.ImageDataUrl),
                q.LastMessageAtUtc,
                Count = q.Messages.Count,
                // ההודעה האחרונה מהצד השני - מחכה לתגובה שלי
                AwaitingMe = q.Messages.LastOrDefault(m => m.From != "Admin") is { } last && last.From != role,
                Unread = IsUnread(q, role),
                LastText = q.Messages.LastOrDefault(m => m.From != role) is { } other
                    ? (other.Text.Length > 90 ? other.Text[..90] + "..." : other.Text)
                    : null
            });
        }
        return Ok(rows);
    }

    private static bool IsUnread(ItemQuestion q, string role)
    {
        var readAt = role == "Owner" ? q.OwnerReadAtUtc : q.RenterReadAtUtc;
        if (readAt is null)
            return q.Messages.LastOrDefault(m => m.From != "Admin") is { } last && last.From != role;
        return q.Messages.Any(m => m.From != role && m.AtUtc > readAt);
    }

    private async Task<(ItemQuestion? Thread, string? Role)> ResolveAsync(string id, string? token)
    {
        var thread = await _questions.GetByIdAsync(id);
        if (thread is null)
            return (null, null);
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(token) && token == thread.OwnerToken || userId == thread.OwnerId)
            return (thread, "Owner");
        if (!string.IsNullOrEmpty(token) && token == thread.RenterToken || userId == thread.RenterId)
            return (thread, "Renter");
        return (thread, null);
    }

    [AllowAnonymous]
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, [FromQuery] string? t)
    {
        var (thread, role) = await ResolveAsync(id, t);
        if (thread is null || role is null)
            return NotFound(new { Message = "ההתכתבות לא נמצאה" });
        if (IsUnread(thread, role))
        {
            if (role == "Owner")
                thread.OwnerReadAtUtc = DateTime.UtcNow;
            else
                thread.RenterReadAtUtc = DateTime.UtcNow;
            await _questions.UpdateAsync(thread);
        }
        var item = await _items.GetByIdAsync(thread.ItemId);
        return Ok(new
        {
            thread.Id,
            Role = role,
            Item = item is null || item.DeletedAtUtc is not null
                ? null
                : new { item.Id, item.Name, Image = ImageLinks.Thumb(item.Id, item.ImageDataUrl), item.City },
            ViewToken = role == "Owner" ? thread.OwnerToken : thread.RenterToken,
            Messages = thread.Messages.Select(m => new { m.From, m.Text, m.AtUtc, m.ImageIds })
        });
    }

    public class AdminMessageDto
    {
        public string Text { get; set; } = string.Empty;
    }

    // הודעת מערכת מהנהלת האתר בתוך ההתכתבות - נשלח מייל לשתיהן
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/admin-message")]
    public async Task<IActionResult> AdminMessage(string id, [FromBody] AdminMessageDto dto)
    {
        var thread = await _questions.GetByIdAsync(id);
        if (thread is null)
            return NotFound(new { Message = "ההתכתבות לא נמצאה" });
        var text = (dto.Text ?? string.Empty).Trim();
        if (text.Length < 1 || text.Length > MaxLength)
            return BadRequest(new { Message = "יש לכתוב הודעה (עד 1000 תווים)" });
        var now = DateTime.UtcNow;
        thread.Messages.Add(new ItemQuestionMessage { From = "Admin", Text = text, AtUtc = now });
        thread.LastMessageAtUtc = now;
        await _questions.UpdateAsync(thread);
        var item = await _items.GetByIdAsync(thread.ItemId);
        await NotifyAsync(thread, item, toOwner: true, first: false, fromAdmin: true);
        await NotifyAsync(thread, item, toOwner: false, first: false, fromAdmin: true);
        return NoContent();
    }

    // תמונה מההתכתבות - טעינת תמונה רגילה עם הקישור האישי (כמו תמונה במייל), לא מקישור ציבורי
    [AllowAnonymous]
    [HttpGet("{id}/images/{imageId}")]
    public async Task<IActionResult> Image(string id, string imageId, [FromQuery] string? t)
    {
        var (thread, role) = await ResolveAsync(id, t);
        if (thread is null || role is null)
            return NotFound();
        if (await _questions.GetImageAsync(imageId) is not { } image || image.ThreadId != thread.Id)
            return NotFound();
        var comma = image.DataUrl.IndexOf(',');
        if (comma < 0)
            return NotFound();
        var mime = image.DataUrl[5..image.DataUrl.IndexOf(';')];
        Response.Headers.CacheControl = "private, max-age=86400";
        return File(Convert.FromBase64String(image.DataUrl[(comma + 1)..]), mime);
    }

    [AllowAnonymous]
    [HttpPost("{id}/messages")]
    public async Task<IActionResult> Reply(string id, [FromQuery] string? t, [FromBody] TextDto dto)
    {
        var (thread, role) = await ResolveAsync(id, t);
        if (thread is null || role is null)
            return NotFound(new { Message = "ההתכתבות לא נמצאה" });
        var text = (dto.Text ?? string.Empty).Trim();
        if (text.Length < 1 && dto.Images is not { Count: > 0 })
            return BadRequest(new { Message = "יש לכתוב הודעה" });
        if (ImagesError(dto.Images) is { } imagesError)
            return BadRequest(new { Message = imagesError });
        if (text.Length > MaxLength)
            return BadRequest(new { Message = "ההודעה ארוכה מדי" });
        if (thread.Messages.Count >= MaxMessages)
            return BadRequest(new { Message = "ההתכתבות הזו הגיעה למקסימום הודעות" });

        var now = DateTime.UtcNow;
        thread.Messages.Add(new ItemQuestionMessage { From = role, Text = Clean(text, thread.Messages.LastOrDefault(m => m.From != role)?.Text), AtUtc = now, ImageIds = await SaveImagesAsync(thread.Id!, dto.Images) });
        if (role == "Owner")
            thread.OwnerReadAtUtc = now;
        else
            thread.RenterReadAtUtc = now;
        thread.LastMessageAtUtc = now;
        await _questions.UpdateAsync(thread);

        var item = await _items.GetByIdAsync(thread.ItemId);
        await NotifyAsync(thread, item, toOwner: role == "Renter", first: false);
        return NoContent();
    }

    private async Task NotifyAsync(ItemQuestion thread, Item? item, bool toOwner, bool first, bool fromAdmin = false)
    {
        if (!_email.IsEnabled)
            return;
        try
        {
            var recipient = await _users.GetByIdAsync(toOwner ? thread.OwnerId : thread.RenterId);
            if (recipient is null || string.IsNullOrWhiteSpace(recipient.Email))
                return;
            string H(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var itemName = item?.Name ?? "הפריט";
            var link = $"{_client.BaseUrl.TrimEnd('/')}/questions/{thread.Id}?t={(toOwner ? thread.OwnerToken : thread.RenterToken)}";
            // נושא קבוע לכל נמענת + References משותף - כל המיילים של אותה התכתבות מגיעים כשרשור אחד
            var subject = toOwner
                ? $"היי {NameText.FirstName(recipient.Name)}, צלמת השאירה לך שאלה באתר COMMON בנוגע ל{itemName}"
                : $"התקבלה לך תשובה בנוגע ל{itemName} - common";
            var intro = fromAdmin
                ? $"התקבלה הודעה מהנהלת האתר בהתכתבות על <strong>{H(itemName)}</strong>."
                : toOwner
                ? (first ? $"צלמת השאירה לך שאלה בנוגע ל<strong>{H(itemName)}</strong>." : $"התקבלה הודעה חדשה בנוגע ל<strong>{H(itemName)}</strong>.")
                : $"התקבלה לך תשובה מהמשכירה בנוגע ל<strong>{H(itemName)}</strong>.";
            var threadRef = $"<question-{thread.Id}-{(toOwner ? "o" : "r")}@commonrent.web.app>";
            var headers = new Dictionary<string, string> { ["References"] = threadRef, ["In-Reply-To"] = threadRef };
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>שלום {H(NameText.FirstName(recipient.Name))},</p>" +
                       $"<p>{intro}</p>" +
                       (thread.Messages.LastOrDefault()?.ImageIds.Count > 0 ? "<p>התמונות שנשלחו בהתכתבות מצורפות למייל הזה.</p>" : string.Empty) +
                       $"<p><a href=\"{H(link)}\" style=\"display:inline-block;background:#a8e29c;border:1px solid #000;border-radius:20px;padding:10px 20px;color:#000;text-decoration:none\"><strong>{(fromAdmin ? "לצפייה בהודעה" : toOwner ? "לצפייה ולתשובה" : "לצפייה בתשובה")}</strong></a></p>" +
                       "<p>common</p></div>";
            var attachments = new List<EmailAttachment>();
            foreach (var imageId in thread.Messages.LastOrDefault()?.ImageIds ?? new List<string>())
            {
                var image = await _questions.GetImageAsync(imageId);
                var m = image is null ? null : Regex.Match(image.Value.DataUrl, @"^data:image/(\w+);base64,(.+)$", RegexOptions.Singleline);
                if (m is { Success: true })
                    attachments.Add(new EmailAttachment($"image-{attachments.Count + 1}.{(m.Groups[1].Value == "jpeg" ? "jpg" : m.Groups[1].Value)}", m.Groups[2].Value));
            }
            await _email.SendAsync(recipient.Email, recipient.Name, subject, html, attachments, null, headers);
        }
        catch
        {
            // מייל שלא נשלח לא מבטל את ההודעה
        }
    }
}
