namespace PhotoRental.Service.Services;

using PhotoRental.Core.DTOs;
using PhotoRental.Core.Entities;
using PhotoRental.Core.Interfaces;

// שכבת הלוגיקה העסקית לטיפול בפניות מדף "יצירת קשר"
public class ContactMessageService : IContactMessageService
{
    // תיבת הדואר של האתר: כל פנייה מטופס "צור קשר" נשמרת וגם נשלחת אליה, והתשובה (Reply) חוזרת לפונה
    private const string InboxEmail = "common@onyxapp.co.il";

    private readonly IContactMessageRepository _contactMessageRepository;
    private readonly IEmailService _emailService;

    public ContactMessageService(IContactMessageRepository contactMessageRepository, IEmailService emailService)
    {
        _contactMessageRepository = contactMessageRepository;
        _emailService = emailService;
    }

    public async Task SendAsync(ContactMessageCreateDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new ArgumentException("יש להזין שם מלא");
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw new ArgumentException("יש להזין כתובת אימייל");
        if (string.IsNullOrWhiteSpace(dto.Message))
            throw new ArgumentException("יש להזין תוכן הודעה");

        await _contactMessageRepository.CreateAsync(new ContactMessage
        {
            Name = dto.Name,
            Email = dto.Email,
            Message = dto.Message,
            CreatedAtUtc = DateTime.UtcNow
        });

        // שליחה לתיבת האתר - כשל במייל לא מכשיל את הפנייה (היא כבר נשמרה)
        try
        {
            var h = (string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);
            var html = "<div dir=\"rtl\" style=\"font-family:Arial,sans-serif\">" +
                       $"<p>פנייה חדשה מטופס \"צור קשר\" באתר:</p>" +
                       $"<p><strong>שם:</strong> {h(dto.Name)}<br/><strong>אימייל:</strong> {h(dto.Email)}</p>" +
                       $"<p style=\"white-space:pre-wrap\">{h(dto.Message)}</p>" +
                       "<p style=\"color:#666\">אפשר להשיב ישירות למייל הזה - התשובה תגיע לפונה.</p></div>";
            await _emailService.SendAsync(InboxEmail, "common", $"פנייה חדשה מ-{dto.Name} - common", html, null, dto.Email);
        }
        catch
        {
            // ממשיכים - הפנייה נשמרה במסד הנתונים
        }
    }
}
