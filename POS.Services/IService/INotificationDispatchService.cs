namespace POS.Services.IService;

/// <summary>
/// Drains due NotificationOutbox rows and sends them via <see cref="IEmailService"/>, applying
/// exponential backoff on transient failures and marking permanent failures Failed. Extracted
/// from the worker loop so it is unit-testable with a fake email sender.
/// </summary>
public interface INotificationDispatchService
{
    /// <summary>Processes one batch of due Pending rows. Returns the number processed.</summary>
    Task<int> DispatchPendingAsync(CancellationToken ct = default);
}
