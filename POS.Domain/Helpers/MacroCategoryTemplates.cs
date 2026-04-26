namespace POS.Domain.Helpers;

/// <summary>
/// Default tenant seed templates keyed by <see cref="MacroCategoryIds"/>. Each template
/// defines the initial categories and sample products applied to a fresh branch so a
/// newly registered tenant lands on a non-empty POS the first time they log in.
/// Templates are intentionally code-defined: changes ship as a PR, not a SQL migration.
/// Promote to a DB-backed catalog only if/when business operations need to edit them.
/// </summary>
public static class MacroCategoryTemplates
{
    public sealed record SeedCategory(string Name, string Icon, int SortOrder);

    /// <summary>
    /// <paramref name="PriceCents"/> matches <c>Product.PriceCents</c> (centavos).
    /// <paramref name="Metadata"/> carries vertical-specific JSON (e.g.
    /// <c>{"MembershipDurationDays":30}</c> for the gym vertical).
    /// </summary>
    public sealed record SeedProduct(
        string Name,
        int PriceCents,
        string CategoryName,
        string? Metadata = null);

    public sealed class MacroSeedTemplate
    {
        public IReadOnlyList<SeedCategory> Categories { get; init; } = Array.Empty<SeedCategory>();
        public IReadOnlyList<SeedProduct> Products { get; init; } = Array.Empty<SeedProduct>();
    }

    public static readonly IReadOnlyDictionary<int, MacroSeedTemplate> ByMacroId = new Dictionary<int, MacroSeedTemplate>
    {
        [MacroCategoryIds.FoodBeverage] = new()
        {
            Categories = new[]
            {
                new SeedCategory("Bebidas",   "pi-filter",       1),
                new SeedCategory("Alimentos", "pi-shopping-bag", 2),
            },
            Products = new[]
            {
                new SeedProduct("Agua Natural",         2500,  "Bebidas"),
                new SeedProduct("Hamburguesa Clásica",  12000, "Alimentos"),
            }
        },

        [MacroCategoryIds.Retail] = new()
        {
            Categories = new[]
            {
                new SeedCategory("Bebidas",   "pi-filter",       1),
                new SeedCategory("Abarrotes", "pi-shopping-bag", 2),
            },
            Products = new[]
            {
                new SeedProduct("Coca-Cola 600ml", 2000, "Bebidas"),
            }
        },

        [MacroCategoryIds.Services] = new()
        {
            Categories = new[]
            {
                new SeedCategory("Membresías", "pi-id-card",  1),
                new SeedCategory("Servicios",  "pi-briefcase", 2),
            },
            Products = new[]
            {
                new SeedProduct("Membresía Mensual", 50000, "Membresías",
                    Metadata: """{"MembershipDurationDays":30}"""),
                new SeedProduct("Visita",            8000,  "Servicios"),
            }
        },
    };
}
