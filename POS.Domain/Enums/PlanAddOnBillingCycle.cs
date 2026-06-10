namespace POS.Domain.Enums;

/// <summary>
/// Billing cadence of a <see cref="Models.Catalogs.PlanAddOn"/>. Persisted as a string
/// (<c>HasConversion&lt;string&gt;</c>). v2 seeds only recurring device licenses (Monthly);
/// OneTime one-shot billing is declared but not yet produced by the generation job.
/// </summary>
public enum PlanAddOnBillingCycle
{
    OneTime,
    Monthly,
    Annual
}
