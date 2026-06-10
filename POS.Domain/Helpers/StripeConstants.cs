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

    // PR-2: the base-plan PriceMap + Resolve*/IsBasePlan were retired. The Stripe
    // Price ids now live in the DB-backed `StripePlanPrice` catalog (seeded in
    // DbInitializer from the nested constants above) and the webhook resolves a
    // base price as catalog → custom-metadata → fail-closed. The nested price-id
    // constant classes (Basico/Pro/Enterprise) are kept solely as the seed source.

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
}
