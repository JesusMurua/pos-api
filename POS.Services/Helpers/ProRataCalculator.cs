namespace POS.Services.Helpers;

/// <summary>
/// Mid-cycle proration for manual-rail add-ons (§14). When an add-on is activated after
/// the billing period started, the first invoice charges only the remaining slice of the
/// period: <c>factor = (PeriodEnd - ActivatedAt) / (PeriodEnd - PeriodStart)</c>. Stripe
/// rails prorate natively (create_prorations); this covers the manual rails so a tenant is
/// never overcharged for a partial first cycle. Extracted as a pure function so it is unit
/// testable without the raw-SQL invoice counter.
/// </summary>
public static class ProRataCalculator
{
    /// <summary>
    /// Returns the charge for an add-on on a given invoice period. Full <paramref name="fullCents"/>
    /// unless the add-on was activated strictly inside the period, in which case it is prorated by
    /// the remaining fraction. The factor is clamped to [0, 1].
    /// </summary>
    public static int Compute(int fullCents, DateTime activatedAt, DateTime periodStart, DateTime periodEnd)
    {
        if (activatedAt <= periodStart) return fullCents; // active for the whole period
        if (activatedAt >= periodEnd) return 0;           // activated after the period closed

        var total = (periodEnd - periodStart).Ticks;
        if (total <= 0) return fullCents; // degenerate period — no proration possible

        var remaining = (periodEnd - activatedAt).Ticks;
        var factor = (decimal)remaining / total; // in (0, 1) given the guards above
        return (int)Math.Round(fullCents * factor, MidpointRounding.AwayFromZero);
    }
}
