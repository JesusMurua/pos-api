namespace POS.Domain.Enums;

/// <summary>
/// Human-only user role enumeration. Numeric values are anchored to the
/// 1-based <c>UserRoleCatalog.Id</c> column in the database so that
/// <c>(int)UserRole.X</c> and <c>(UserRole)userRoleId</c> round-trip safely.
/// Do NOT renumber these values without a coordinated DB migration.
/// </summary>
/// <remarks>
/// Hardware identities (KDS Kitchen, self-service Kiosk) used to live here
/// alongside humans, which conflated authentication concerns and triggered
/// the cross-stack ID drift fixed in BDD-018. Those modes are now expressed
/// exclusively through <c>DeviceModeCodes</c> + Device-JWT auth and must
/// never reappear in this enum.
/// </remarks>
public enum UserRole
{
    Owner = 1,
    Manager = 2,
    Cashier = 3,
    Waiter = 4,
    Host = 5
}
