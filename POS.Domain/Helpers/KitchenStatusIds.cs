namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching KitchenStatusCatalog.Id values.
/// </summary>
public static class KitchenStatusIds
{
    public const int Pending = 1;
    public const int Ready = 2;
    public const int Delivered = 3;

    public static int FromString(string? status) => status?.ToLowerInvariant() switch
    {
        "pending" or "preparing" => Pending,
        "ready" => Ready,
        "delivered" => Delivered,
        _ => Pending
    };
}
