using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching <c>UserRoleCatalog.Id</c> values. Now that
/// <see cref="UserRole"/> is anchored to the same 1-based numbering, these
/// constants and the enum are interchangeable — kept here only so existing
/// call sites that read role ids as <c>int</c> stay readable.
/// </summary>
public static class UserRoleIds
{
    public const int Owner = (int)UserRole.Owner;
    public const int Manager = (int)UserRole.Manager;
    public const int Cashier = (int)UserRole.Cashier;
    public const int Kitchen = (int)UserRole.Kitchen;
    public const int Waiter = (int)UserRole.Waiter;
    public const int Kiosk = (int)UserRole.Kiosk;
    public const int Host = (int)UserRole.Host;

    /// <summary>
    /// Trivial wrapper kept for back-compat. Direct cast <c>(int)role</c>
    /// produces the same result now that the enum is DB-aligned.
    /// </summary>
    public static int FromEnum(UserRole role) => (int)role;

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
