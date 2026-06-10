using POS.Services.Helpers;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// Pure unit coverage of mid-cycle add-on proration (§14). Manual rails charge only the
/// remaining slice of the first period when an add-on is activated mid-cycle; the integration
/// with the generation job (which assigns the invoice number via the raw-SQL counter) is
/// review-only on InMemory.
/// </summary>
public class ProRataCalculatorTests
{
    private static readonly DateTime PeriodStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc); // 30-day period

    [Fact]
    public void ActivatedAtPeriodStart_ChargesFull()
    {
        ProRataCalculator.Compute(30000, PeriodStart, PeriodStart, PeriodEnd).Should().Be(30000);
    }

    [Fact]
    public void ActivatedBeforePeriod_ChargesFull()
    {
        ProRataCalculator.Compute(30000, PeriodStart.AddDays(-5), PeriodStart, PeriodEnd).Should().Be(30000);
    }

    [Fact]
    public void ActivatedMidCycle_ProratesByRemainingFraction()
    {
        // Activated at day 20 of a 30-day period → 10/30 remaining.
        var activatedAt = PeriodStart.AddDays(20);
        ProRataCalculator.Compute(30000, activatedAt, PeriodStart, PeriodEnd).Should().Be(10000);
    }

    [Fact]
    public void ActivatedHalfway_ChargesHalf()
    {
        var activatedAt = PeriodStart.AddDays(15); // 15/30 remaining
        ProRataCalculator.Compute(30000, activatedAt, PeriodStart, PeriodEnd).Should().Be(15000);
    }

    [Fact]
    public void ActivatedAtOrAfterPeriodEnd_ChargesZero()
    {
        ProRataCalculator.Compute(30000, PeriodEnd, PeriodStart, PeriodEnd).Should().Be(0);
        ProRataCalculator.Compute(30000, PeriodEnd.AddDays(1), PeriodStart, PeriodEnd).Should().Be(0);
    }
}
