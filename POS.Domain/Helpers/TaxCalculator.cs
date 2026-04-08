namespace POS.Domain.Helpers;

/// <summary>
/// Centralized tax math for CFDI-compliant rounding.
/// All methods operate in cents (integers) to prevent floating-point drift.
/// </summary>
public static class TaxCalculator
{
    /// <summary>
    /// Calculates tax from a price that already includes tax.
    /// Formula: base = Round(total / (1 + rate)), tax = total - base.
    /// </summary>
    /// <param name="lineTotalCents">Total price including tax (UnitPriceCents * Quantity).</param>
    /// <param name="rate">Tax rate as decimal (e.g., 0.16 for 16%).</param>
    /// <returns>Tax amount in cents.</returns>
    public static int CalculateInclusiveTax(int lineTotalCents, decimal rate)
    {
        if (rate <= 0m) return 0;

        var baseCents = Math.Round(lineTotalCents / (1m + rate), MidpointRounding.AwayFromZero);
        return lineTotalCents - (int)baseCents;
    }

    /// <summary>
    /// Calculates tax from a base price that does NOT include tax.
    /// Formula: tax = Round(base * rate).
    /// </summary>
    /// <param name="baseCents">Base price excluding tax.</param>
    /// <param name="rate">Tax rate as decimal (e.g., 0.16 for 16%).</param>
    /// <returns>Tax amount in cents.</returns>
    public static int CalculateExclusiveTax(int baseCents, decimal rate)
    {
        if (rate <= 0m) return 0;

        return (int)Math.Round(baseCents * rate, MidpointRounding.AwayFromZero);
    }
}
