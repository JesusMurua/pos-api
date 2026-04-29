namespace POS.Domain.Enums;

/// <summary>
/// User role enumeration. Numeric values are anchored to the 1-based
/// <c>UserRoleCatalog.Id</c> column in the database so that
/// <c>(int)UserRole.X</c> and <c>(UserRole)userRoleId</c> round-trip
/// safely. Do NOT renumber these values without a coordinated DB migration.
/// </summary>
public enum UserRole
{
    Owner = 1,
    Manager = 2,
    Cashier = 3,
    Kitchen = 4,
    Waiter = 5,
    Kiosk = 6,
    Host = 7
}
