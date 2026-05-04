using POS.Domain.Models;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class TaxResolverService : ITaxResolverService
{
    /// <inheritdoc />
    /// <remarks>
    /// Resolution chain:
    ///  a) <see cref="Product.ProductTaxes"/> — sum of all linked taxes wins.
    ///  b) <see cref="Business.DefaultTax"/> — tenant's resting policy.
    ///  c) Country default — first row in <paramref name="countryDefaults"/>.
    ///  d) Zero — tax-exempt fallback. Returned with <c>Tax == null</c> so
    ///     callers know not to write an <c>OrderItemTax</c> snapshot.
    /// Steps (b)/(c) require the caller to have eager-loaded the navigation
    /// data; this method never hits the database.
    /// </remarks>
    public TaxResolutionResult ResolveTax(
        Product product,
        Business business,
        IReadOnlyList<Tax> countryDefaults)
    {
        // (a) Per-product association — the new relational engine.
        if (product.ProductTaxes is { Count: > 0 })
        {
            var configured = product.ProductTaxes
                .Where(pt => pt.Tax != null)
                .Select(pt => pt.Tax!)
                .ToList();

            if (configured.Count > 0)
            {
                var aggregateRate = configured.Sum(t => t.Rate);
                // Preserve the first Tax as the snapshot reference. Multi-tax
                // products (e.g. IVA + IEPS) currently snapshot the dominant
                // row; the OrderItemTax collection can hold multiple rows
                // when callers iterate ProductTaxes themselves.
                return new TaxResolutionResult(configured[0], aggregateRate, product.IsTaxIncluded);
            }
        }

        // (b) Tenant resting policy.
        if (business.DefaultTax != null)
        {
            return new TaxResolutionResult(business.DefaultTax, business.DefaultTax.Rate, product.IsTaxIncluded);
        }

        // (c) Country default seeded in the Tax catalog.
        var fallback = countryDefaults.FirstOrDefault();
        if (fallback != null)
        {
            return new TaxResolutionResult(fallback, fallback.Rate, product.IsTaxIncluded);
        }

        // (d) Tax-exempt absolute fallback.
        return new TaxResolutionResult(null, 0m, product.IsTaxIncluded);
    }
}
