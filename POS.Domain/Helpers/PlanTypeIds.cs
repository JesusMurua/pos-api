using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching PlanTypeCatalog.Id values.
/// </summary>
public static class PlanTypeIds
{
    public const int Free = 1;
    public const int Basic = 2;
    public const int Pro = 3;
    public const int Enterprise = 4;

    public static int FromEnum(PlanType plan) => plan switch
    {
        PlanType.Free => Free,
        PlanType.Basic => Basic,
        PlanType.Pro => Pro,
        PlanType.Enterprise => Enterprise,
        _ => Free
    };

    public static string ToCode(int id) => id switch
    {
        Free => "Free",
        Basic => "Basic",
        Pro => "Pro",
        Enterprise => "Enterprise",
        _ => "Free"
    };
}
