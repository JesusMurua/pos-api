namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching TableStatusCatalog.Id values.
/// </summary>
public static class TableStatusIds
{
    public const int Available = 1;
    public const int Occupied = 2;
    public const int Reserved = 3;
    public const int Maintenance = 4;
}
