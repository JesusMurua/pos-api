namespace POS.Domain.Models;

/// <summary>
/// DTO returned by GET /api/subscription/status with the current subscription state.
/// </summary>
public class SubscriptionStatusDto
{
    public string PlanType { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string PricingGroup { get; set; } = null!;
    public string BillingCycle { get; set; } = null!;
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? TrialEndsAt { get; set; }
    public bool IsActive { get; set; }
}
