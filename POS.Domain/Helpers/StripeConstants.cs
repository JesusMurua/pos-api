using POS.Domain.Enums;

namespace POS.Domain.Helpers;

// RUNBOOK: Any new Price ID created in the Stripe Dashboard MUST be registered here
// (in PriceMap for base plans, in AddonPriceMap for add-ons) before selling.
// Otherwise the webhook processor will fail-closed and the customer's subscription
// will not sync — see StripeEventProcessorWorker for the strict resolution path.

/// <summary>
/// Stripe Price IDs organized by plan tier, business type pricing group, and billing cycle.
/// All IDs correspond to prices created in the Stripe dashboard (test mode).
/// </summary>
public static class StripeConstants
{
    public static class Basico
    {
        public static class General
        {
            public const string Monthly = "price_1TGVDNGd6oMtnYKN3mOfuloV";
            public const string Annual = "price_1TGVGBGd6oMtnYKNOtYdklZ7";
        }

        public static class Standard
        {
            public const string Monthly = "price_1TGjYIGd6oMtnYKNaWsO5wW9";
            public const string Annual = "price_1TGjYvGd6oMtnYKNNLJSrXWk";
        }

        public static class Restaurant
        {
            public const string Monthly = "price_1TGjZTGd6oMtnYKNKH4mV0WR";
            public const string Annual = "price_1TGjaKGd6oMtnYKNMlQbqt1f";
        }
    }

    public static class Pro
    {
        public static class General
        {
            public const string Monthly = "price_1TGjiaGd6oMtnYKNFY6ZbnMS";
            public const string Annual = "price_1TGjj3Gd6oMtnYKNYX06rZPx";
        }

        public static class Standard
        {
            public const string Monthly = "price_1TGjjMGd6oMtnYKNnUYsOsmr";
            public const string Annual = "price_1TGjk0Gd6oMtnYKNbIyJOpr8";
        }

        public static class Restaurant
        {
            public const string Monthly = "price_1TGVDsGd6oMtnYKNGYySti0z";
            public const string Annual = "price_1TGVFhGd6oMtnYKNJGIXZ3d3";
        }
    }

    public static class Enterprise
    {
        public static class General
        {
            public const string Monthly = "price_1TGjrfGd6oMtnYKNaEVHitCF";
            public const string Annual = "price_1TGjs2Gd6oMtnYKN4BvXPwXw";
        }

        public static class Standard
        {
            public const string Monthly = "price_1TGjsMGd6oMtnYKNV4ixW9ms";
            public const string Annual = "price_1TGjtEGd6oMtnYKNMDlACMO2";
        }

        public static class Restaurant
        {
            public const string Monthly = "price_1TGVEDGd6oMtnYKNC7v50zld";
            public const string Annual = "price_1TGVErGd6oMtnYKNfEBSfiPS";
        }
    }

    /// <summary>
    /// Resolves the Stripe pricing group from the business's macro category id.
    /// Food &amp; Beverage → Restaurant, Quick Service → Standard, everything else → General.
    /// </summary>
    public static string GetPricingGroup(int primaryMacroCategoryId) => primaryMacroCategoryId switch
    {
        MacroCategoryIds.FoodBeverage => "Restaurant",
        MacroCategoryIds.QuickService => "Standard",
        _ => "General"
    };

    /// <summary>
    /// Maps Stripe Price IDs to (PlanType, BillingCycle, PricingGroup) tuples.
    /// </summary>
    public static readonly Dictionary<string, (string Plan, string Cycle, string Group)> PriceMap = new()
    {
        // Basico
        { Basico.General.Monthly, ("Basico", "Monthly", "General") },
        { Basico.General.Annual, ("Basico", "Annual", "General") },
        { Basico.Standard.Monthly, ("Basico", "Monthly", "Standard") },
        { Basico.Standard.Annual, ("Basico", "Annual", "Standard") },
        { Basico.Restaurant.Monthly, ("Basico", "Monthly", "Restaurant") },
        { Basico.Restaurant.Annual, ("Basico", "Annual", "Restaurant") },
        // Pro
        { Pro.General.Monthly, ("Pro", "Monthly", "General") },
        { Pro.General.Annual, ("Pro", "Annual", "General") },
        { Pro.Standard.Monthly, ("Pro", "Monthly", "Standard") },
        { Pro.Standard.Annual, ("Pro", "Annual", "Standard") },
        { Pro.Restaurant.Monthly, ("Pro", "Monthly", "Restaurant") },
        { Pro.Restaurant.Annual, ("Pro", "Annual", "Restaurant") },
        // Enterprise
        { Enterprise.General.Monthly, ("Enterprise", "Monthly", "General") },
        { Enterprise.General.Annual, ("Enterprise", "Annual", "General") },
        { Enterprise.Standard.Monthly, ("Enterprise", "Monthly", "Standard") },
        { Enterprise.Standard.Annual, ("Enterprise", "Annual", "Standard") },
        { Enterprise.Restaurant.Monthly, ("Enterprise", "Monthly", "Restaurant") },
        { Enterprise.Restaurant.Annual, ("Enterprise", "Annual", "Restaurant") },
    };

    /// <summary>
    /// Resolves a Stripe Price ID to its base plan name. Throws
    /// <see cref="KeyNotFoundException"/> when the id is not present in
    /// <see cref="PriceMap"/> — fail-closed semantics force a registry update
    /// instead of silently bucketing unknown prices into "Free", which used
    /// to downgrade tenants invisibly when Stripe drift occurred.
    /// </summary>
    public static string ResolvePlanType(string priceId)
    {
        if (!PriceMap.TryGetValue(priceId, out var info))
            throw new KeyNotFoundException(
                $"Stripe Price ID '{priceId}' is not registered as a base plan in StripeConstants.PriceMap. Add it to the catalog before processing this subscription.");
        return info.Plan;
    }

    public static int ResolvePlanTypeId(string priceId) =>
        PlanTypeIds.FromEnum(Enum.TryParse<Enums.PlanType>(ResolvePlanType(priceId), true, out var p) ? p : Enums.PlanType.Free);

    public static string ResolveBillingCycle(string priceId)
    {
        if (!PriceMap.TryGetValue(priceId, out var info))
            throw new KeyNotFoundException(
                $"Stripe Price ID '{priceId}' is not registered as a base plan in StripeConstants.PriceMap.");
        return info.Cycle;
    }

    public static string ResolvePricingGroup(string priceId)
    {
        if (!PriceMap.TryGetValue(priceId, out var info))
            throw new KeyNotFoundException(
                $"Stripe Price ID '{priceId}' is not registered as a base plan in StripeConstants.PriceMap.");
        return info.Group;
    }

    /// <summary>
    /// Catalog of Add-on Price IDs (extra device licenses sold on top of a
    /// base plan). Each entry maps to a <see cref="FeatureKey"/> and a
    /// <c>QuantityPerUnit</c> indicating how many license units the add-on
    /// grants per purchased Stripe quantity.
    /// </summary>
    /// <remarks>
    /// Real Stripe Price IDs replace these placeholders once the dashboard
    /// products are created. The placeholder ids are intentionally
    /// unconventional (no <c>price_1</c> prefix) so they cannot collide with
    /// real Stripe ids by accident.
    /// </remarks>
    public static readonly Dictionary<string, (FeatureKey Feature, int QuantityPerUnit)> AddonPriceMap = new()
    {
        { "price_dummy_kds",      (FeatureKey.MaxKdsScreens,      1) },
        { "price_dummy_kiosk",    (FeatureKey.MaxKiosks,          1) },
        { "price_dummy_cashier",  (FeatureKey.MaxCashRegisters,   1) },
    };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="priceId"/> is a registered
    /// add-on price. Used by the webhook handler to classify each item in
    /// <c>stripe_subscription.items.data</c> as base-plan vs add-on.
    /// </summary>
    public static bool IsAddon(string priceId) => AddonPriceMap.ContainsKey(priceId);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="priceId"/> is a registered
    /// base plan price.
    /// </summary>
    public static bool IsBasePlan(string priceId) => PriceMap.ContainsKey(priceId);
}
