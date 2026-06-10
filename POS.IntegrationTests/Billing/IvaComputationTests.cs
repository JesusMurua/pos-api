using POS.Services.Helpers;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// Pure unit coverage of the backend IVA engine (OQ-8). Manual-rail invoices compute tax
/// here; Stripe-rail invoices never do (they copy Stripe's amounts) — see
/// docs/saas-billing-architecture.md §7.
/// </summary>
public class IvaComputationTests
{
    private const decimal MxRate = 0.16m;

    [Fact]
    public void Mxn_AppliesSixteenPercent()
    {
        var (subtotal, tax, total) = BillingTaxCalculator.Compute(10000, "MXN", MxRate);
        subtotal.Should().Be(10000);
        tax.Should().Be(1600);
        total.Should().Be(11600);
    }

    [Fact]
    public void FullDiscount_ZeroSubtotal_ZeroTax()
    {
        var (subtotal, tax, total) = BillingTaxCalculator.Compute(0, "MXN", MxRate);
        subtotal.Should().Be(0);
        tax.Should().Be(0);
        total.Should().Be(0);
    }

    [Fact]
    public void NegativeNetSubtotal_TaxAppliesPostDiscount()
    {
        // Subtotal already nets a discount line; IVA applies to the discounted net.
        var (subtotal, tax, total) = BillingTaxCalculator.Compute(-5000, "MXN", MxRate);
        subtotal.Should().Be(-5000);
        tax.Should().Be(-800);
        total.Should().Be(-5800);
    }

    [Fact]
    public void NonMxnCurrency_FallsBackToZeroRate()
    {
        // OQ-10: foreign-currency tax is explicit debt — no rate assumed.
        var (subtotal, tax, total) = BillingTaxCalculator.Compute(10000, "USD", MxRate);
        subtotal.Should().Be(10000);
        tax.Should().Be(0);
        total.Should().Be(10000);
    }
}
