namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching PaymentStatusCatalog.Id values.
/// </summary>
public static class PaymentStatus
{
    public const int Pending = 1;
    public const int Completed = 2;
    public const int Failed = 3;
    public const int Refunded = 4;

    public static bool IsValid(int statusId) => statusId is >= 1 and <= 4;

    /// <summary>
    /// Maps a legacy string status (from frontend/API) to the catalog integer Id.
    /// </summary>
    public static int FromString(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" => Pending,
        "completed" => Completed,
        "failed" => Failed,
        "refunded" => Refunded,
        _ => Pending
    };
}
