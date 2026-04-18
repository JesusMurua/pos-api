namespace POS.Domain.Helpers;

/// <summary>
/// Integer constants matching <c>MacroCategory.Id</c> seeded values.
/// Driving classification for POS experience, feature gating and pricing.
/// </summary>
public static class MacroCategoryIds
{
    public const int FoodBeverage = 1;
    public const int QuickService = 2;
    public const int Retail = 3;
    public const int Services = 4;

    public const string FoodBeverageCode = "food-beverage";
    public const string QuickServiceCode = "quick-service";
    public const string RetailCode = "retail";
    public const string ServicesCode = "services";

    public static string ToCode(int id) => id switch
    {
        FoodBeverage => FoodBeverageCode,
        QuickService => QuickServiceCode,
        Retail => RetailCode,
        Services => ServicesCode,
        _ => RetailCode
    };

    public static int FromCode(string? code) => code switch
    {
        FoodBeverageCode => FoodBeverage,
        QuickServiceCode => QuickService,
        RetailCode => Retail,
        ServicesCode => Services,
        _ => 0
    };

    /// <summary>Whether the macro category drives a kitchen-centric POS layout.</summary>
    public static bool HasKitchen(int macroCategoryId) =>
        macroCategoryId is FoodBeverage or QuickService;

    /// <summary>Whether the macro category drives a table/dine-in POS layout.</summary>
    public static bool HasTables(int macroCategoryId) =>
        macroCategoryId == FoodBeverage;

    /// <summary>POS experience variant the frontend should render for this macro.</summary>
    public static string PosExperience(int macroCategoryId) => macroCategoryId switch
    {
        FoodBeverage => "Restaurant",
        QuickService => "Counter",
        Retail => "Retail",
        Services => "Quick",
        _ => "Quick"
    };

    /// <summary>Stripe pricing group anchored to the macro category.</summary>
    public static string PricingGroup(int macroCategoryId) => macroCategoryId switch
    {
        FoodBeverage => "Restaurant",
        QuickService => "Standard",
        _ => "General"
    };
}
