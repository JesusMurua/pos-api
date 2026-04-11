using POS.Domain.Enums;

namespace POS.Services.IService;

/// <summary>
/// Evaluates the Plan × BusinessType feature matrix for a tenant.
/// Feature enablement requires both: (1) the feature is applicable to at least one of the
/// business's giros, and (2) the business's plan enables the feature.
/// Quantitative features additionally enforce a current-usage cap.
/// </summary>
public interface IFeatureGateService
{
    /// <summary>
    /// Returns true when the feature is enabled for the business under its current plan and giros.
    /// For quantitative features this only reports plan + applicability — it does NOT inspect usage.
    /// </summary>
    Task<bool> IsEnabledAsync(int businessId, FeatureKey feature);

    /// <summary>
    /// Returns the resolved numeric limit for a quantitative feature.
    /// Null means unlimited. Returns null for boolean features too.
    /// </summary>
    Task<int?> GetLimitAsync(int businessId, FeatureKey feature);

    /// <summary>
    /// Throws PlanLimitExceededException if the feature is disabled OR, for quantitative
    /// features, the given current usage has reached the resolved limit.
    /// Use from POST/PUT paths; GETs should stay untouched (soft enforcement).
    /// </summary>
    Task EnforceAsync(int businessId, FeatureKey feature, int? currentUsage = null);

    /// <summary>
    /// Invalidates the cached matrix for a specific business (call on plan changes, giro changes).
    /// </summary>
    void Invalidate(int businessId);
}
