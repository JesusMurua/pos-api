namespace POS.Domain.Helpers;

/// <summary>
/// Constants for payment status values stored in OrderPayment.Status.
/// </summary>
public static class PaymentStatus
{
    public const string Completed = "completed";
    public const string Pending = "pending";
    public const string Failed = "failed";
    public const string Refunded = "refunded";

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Completed, Pending, Failed, Refunded
    };

    public static bool IsValid(string? status) => status is not null && ValidStatuses.Contains(status);
}
