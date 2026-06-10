namespace POS.Domain.Enums;

/// <summary>
/// Outcome of a single email send attempt, used by the dispatcher to decide retry vs fail.
/// Not persisted. <see cref="TransientError"/> is retried with backoff; <see cref="PermanentError"/>
/// goes straight to Failed.
/// </summary>
public enum EmailSendResult
{
    Sent,
    TransientError,
    PermanentError
}
