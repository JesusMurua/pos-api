using POS.Domain.Enums;

namespace POS.Services.IService;

/// <summary>
/// Low-level transactional email sender (Resend). Called ONLY by the
/// <c>NotificationDispatchService</c> — business code enqueues via
/// <see cref="INotificationService"/> instead of sending directly (PR-5).
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends one email now and reports the outcome so the dispatcher can decide retry vs fail.
    /// Returns <see cref="EmailSendResult.TransientError"/> for retryable failures (5xx, network,
    /// timeout, AND HTTP 429 rate-limit) and <see cref="EmailSendResult.PermanentError"/> only for
    /// non-retryable client errors. Never throws.
    /// </summary>
    Task<EmailSendResult> SendNowAsync(string toEmail, string subject, string bodyHtml, string bodyText);
}
