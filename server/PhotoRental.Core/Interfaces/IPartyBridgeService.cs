namespace PhotoRental.Core.Interfaces;

public interface IPartyBridgeService
{
    string? CommonNumber { get; }

    bool CallbackEnabled { get; }

    Task StartCallbackAsync(string userId, string loanId);

    Task<string> HandleIncomingAsync(string fromPhone);

    Task<string> HandleSelectionAsync(string fromPhone, string digits);

    Task<string> CompleteAsync(string logId, string dialStatus, int durationSeconds);

    Task<string> WhisperAsync(string logId);

    Task CallbackFromLinkAsync(string logId, string token);
}
