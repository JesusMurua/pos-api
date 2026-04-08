using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching PromotionTypeCatalog.Id values.
/// </summary>
public static class PromotionTypeIds
{
    public const int Percentage = 1;
    public const int Fixed = 2;
    public const int Bogo = 3;
    public const int Bundle = 4;
    public const int OrderDiscount = 5;
    public const int FreeProduct = 6;

    public static int FromEnum(PromotionType type) => type switch
    {
        PromotionType.Percentage => Percentage,
        PromotionType.Fixed => Fixed,
        PromotionType.Bogo => Bogo,
        PromotionType.Bundle => Bundle,
        PromotionType.OrderDiscount => OrderDiscount,
        PromotionType.FreeProduct => FreeProduct,
        _ => Percentage
    };
}
