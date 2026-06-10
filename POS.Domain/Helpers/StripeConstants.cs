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
    //
    // PR-4: the static AddonPriceMap + IsAddon were retired. Add-ons now live in the
    // DB-backed `PlanAddOn` catalog (seeded in DbInitializer); the webhook classifies an
    // item as an add-on when its price id is in PlanAddOn.StripePriceId.

    /// <summary>The Stripe Price id placeholders for the seeded device-license add-ons.
    /// Kept solely as the seed source for the PlanAddOn catalog (DbInitializer).</summary>
    public static class AddOnPlaceholders
    {
        public const string Kds = "price_dummy_kds";
        public const string Kiosk = "price_dummy_kiosk";
        public const string Cashier = "price_dummy_cashier";
    }
}
