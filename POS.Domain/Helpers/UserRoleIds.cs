using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching UserRoleCatalog.Id values.
/// </summary>
public static class UserRoleIds
{
    public const int Owner = 1;
    public const int Manager = 2;
    public const int Cashier = 3;
    public const int Kitchen = 4;
    public const int Waiter = 5;
    public const int Kiosk = 6;
    public const int Host = 7;

    public static int FromEnum(UserRole role) => role switch
    {
        UserRole.Owner => Owner,
        UserRole.Manager => Manager,
        UserRole.Cashier => Cashier,
        UserRole.Kitchen => Kitchen,
        UserRole.Waiter => Waiter,
        UserRole.Kiosk => Kiosk,
        UserRole.Host => Host,
        _ => Cashier
    };

    public static string ToCode(int id) => id switch
    {
        Owner => "Owner",
        Manager => "Manager",
        Cashier => "Cashier",
        Kitchen => "Kitchen",
        Waiter => "Waiter",
        Kiosk => "Kiosk",
        Host => "Host",
        _ => "Cashier"
    };

    /// <summary>
    /// True when the role has full Back Office access (Owner or Manager). Legacy
    /// DB rows tagged as "Admin" are still id=2 and therefore map to Manager,
    /// so this helper is the single source of truth for admin-level checks.
    /// </summary>
    public static bool IsAdminRole(int roleId) => roleId == Owner || roleId == Manager;
}
