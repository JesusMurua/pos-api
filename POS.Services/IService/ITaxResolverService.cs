using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Pure functional resolver for the tenant tax policy. The single decision
/// point that translates the (Product, Business, Country defaults) tuple
/// into the rate and snapshot reference applied to a sale.
/// </summary>
/// <remarks>
/// Stateless on purpose — callers are responsible for batch-loading the
/// inputs once per request/sync to prevent N+1 access. The resolver only
/// inspects the in-memory graph it is given.
/// </remarks>
public interface ITaxResolverService
{
    /// <summary>
    /// Resolves the tax that applies to a sale of <paramref name="product"/>
    /// under the policy of <paramref name="business"/>.
    /// </summary>
    /// <param name="product">
    /// The product being sold. Must have <see cref="Product.ProductTaxes"/>
    /// (with <c>Tax</c> populated) eagerly loaded if any are configured.
    /// </param>
    /// <param name="business">
    /// The owning tenant. <see cref="Business.DefaultTax"/> must be eagerly
    /// loaded for the second fallback step to fire.
    /// </param>
    /// <param name="countryDefaults">
    /// Pre-loaded <see cref="Tax"/> rows where <c>IsDefault == true</c> for
    /// <paramref name="business"/>'s country. Pass an empty list when none
    /// exist; the resolver will then return zero.
    /// </param>
    TaxResolutionResult ResolveTax(
        Product product,
        Business business,
        IReadOnlyList<Tax> countryDefaults);
}

/// <summary>
/// Outcome of <see cref="ITaxResolverService.ResolveTax"/>. Carries enough
/// information for the caller to write a complete <c>OrderItemTax</c>
/// snapshot when the resolved <see cref="Rate"/> is non-zero.
/// </summary>
/// <param name="Tax">
/// The <see cref="Tax"/> entity that produced <see cref="Rate"/>. Always
/// non-null for non-zero rates; null only when the chain falls through
/// to the absolute zero fallback.
/// </param>
/// <param name="Rate">
/// Effective tax rate as a decimal (0.16 for 16%). Zero means tax-exempt.
/// </param>
/// <param name="IsInclusive">
/// Mirrors <see cref="Product.IsTaxIncluded"/>. Drives whether the caller
/// treats <c>UnitPriceCents</c> as gross or net.
/// </param>
public record TaxResolutionResult(Tax? Tax, decimal Rate, bool IsInclusive);
