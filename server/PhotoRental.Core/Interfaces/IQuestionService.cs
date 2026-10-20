using PhotoRental.Core.DTOs;

namespace PhotoRental.Core.Interfaces;

public interface IQuestionService
{
    Task<QuestionCreatedDto> AskAsync(string userId, string? itemId, string? text, List<string>? images);

    Task<IReadOnlyList<QuestionSummaryDto>> MineAsync(string userId);

    // null = ההתכתבות לא נמצאה או שאין הרשאה לצפות בה
    Task<QuestionThreadDto?> GetAsync(string id, string? token, string? userId);

    Task AddAdminMessageAsync(string id, string? text);

    Task<QuestionImageFile?> GetImageAsync(string id, string imageId, string? token, string? userId);

    Task ReplyAsync(string id, string? token, string? userId, string? text, List<string>? images);

    Task<IReadOnlyList<AdminQuestionDto>> GetAllForAdminAsync();

    // עריכת הודעה (למשל פרטי קשר שנכתבו). טקסט ריק - מחיקת ההודעה
    Task EditMessageAsync(string id, int index, string? text);
}
