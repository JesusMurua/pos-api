namespace POS.Domain.Enums;

/// <summary>
/// Scope used by the device-licensing enforcement engine to decide how to
/// aggregate the consumption count for a quantitative feature.
/// </summary>
public enum EnforcementScope
{
    /// <summary>
    /// The limit applies across the entire business. Devices are counted at
    /// the tenant level regardless of which branch they live in (e.g.
    /// <c>MaxCashRegisters</c>, <c>MaxKdsScreens</c>, <c>MaxKiosks</c>).
    /// </summary>
    Global = 0,

    /// <summary>
    /// The limit applies per branch. Devices are counted within the branch
    /// being queried (e.g. <c>MaxReceptionsPerBranch</c>).
    /// </summary>
    Branch = 1
}
