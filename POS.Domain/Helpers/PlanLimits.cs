namespace POS.Domain.Helpers;

/// <summary>
/// Quantitative limits enforced per plan tier.
/// Values match the Business Rules Manual (Section 3).
/// </summary>
public static class PlanLimits
{
    public const int FreeMaxUsers = 3;
    public const int FreeMaxProducts = 100;
    public const int FreeMaxBranches = 1;

    public const int ProMaxBranches = 3;
}
