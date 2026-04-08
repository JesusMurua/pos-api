namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching CashRegisterStatusCatalog.Id values.
/// </summary>
public static class CashRegisterStatus
{
    public const int Open = 1;
    public const int Closed = 2;
    public const int Auditing = 3;
}
