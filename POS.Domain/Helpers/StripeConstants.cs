namespace POS.Domain.Helpers;

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
    /// Resolves the pricing group based on the business type.
    /// Restaurant/Bar → Restaurant, Cafe/FoodTruck/Taqueria → Standard, everything else → General.
    /// </summary>
    public static string GetPricingGroup(string businessType) =>
        businessType switch
        {
            "Restaurant" or "Bar" => "Restaurant",
            "Cafe" or "FoodTruck" or "Taqueria" => "Standard",
            _ => "General"
        };
}
