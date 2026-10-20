using PhotoRental.Core.DTOs;

namespace PhotoRental.Core.Interfaces;

public interface IItemMediaService
{
    // slot: "thumb" או מספר התמונה. null = אין תמונה כזו
    Task<MediaFile?> GetImageAsync(string itemId, string slot);

    Task<SharePreview> GetSharePreviewAsync(string itemId);

    // התמונה הראשית לתצוגה המקדימה. null = להפנות לתמונת השיתוף הכללית
    Task<MediaFile?> GetShareImageAsync(string itemId);

    string DefaultShareImageUrl { get; }
}
