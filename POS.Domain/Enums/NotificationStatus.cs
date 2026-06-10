namespace POS.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Models.NotificationOutbox"/> row. Persisted as a string.
/// <c>Failed</c> = the retry cap was reached or a permanent error occurred;
/// <c>Cancelled</c> = an operator cancelled a Pending row before it was sent.
/// </summary>
public enum NotificationStatus
{
    Pending,
    Sent,
    Failed,
    Cancelled
}
