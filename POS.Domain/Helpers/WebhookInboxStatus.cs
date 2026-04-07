namespace POS.Domain.Helpers;

/// <summary>
/// Constants for payment webhook inbox status values.
/// </summary>
public static class WebhookInboxStatus
{
    public const string Pending = "pending";
    public const string Processed = "processed";
    public const string Failed = "failed";
}
