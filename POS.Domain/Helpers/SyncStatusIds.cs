namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching OrderSyncStatusCatalog.Id values.
/// </summary>
public static class SyncStatusIds
{
    public const int Pending = 1;
    public const int Synced = 2;
    public const int Failed = 3;
}
