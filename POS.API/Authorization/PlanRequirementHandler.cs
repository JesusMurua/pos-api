using Microsoft.AspNetCore.Authorization;
using POS.Domain.Enums;

namespace POS.API.Authorization;

/// <summary>
/// Authorization requirement: business must have at least the specified plan tier.
/// Backs the legacy RequiresPlan_* policies used by controllers that still rely on
/// AuthorizeAttribute-based plan gating. Phase 4 will migrate these to
/// <see cref="POS.API.Filters.RequiresFeatureAttribute"/> and this file will be removed.
/// </summary>
public class PlanRequirement : IAuthorizationRequirement
{
    public PlanType MinimumPlan { get; }

    public PlanRequirement(PlanType minimumPlan)
    {
        MinimumPlan = minimumPlan;
    }
}

public class PlanRequirementHandler : AuthorizationHandler<PlanRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PlanRequirement requirement)
    {
        var planClaim = context.User.FindFirst("planType")?.Value;

        if (!string.IsNullOrEmpty(planClaim)
            && Enum.TryParse<PlanType>(planClaim, true, out var userPlan)
            && userPlan >= requirement.MinimumPlan)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
