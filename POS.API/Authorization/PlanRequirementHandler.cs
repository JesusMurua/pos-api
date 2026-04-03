using Microsoft.AspNetCore.Authorization;
using POS.Domain.Enums;

namespace POS.API.Authorization;

/// <summary>
/// Authorization requirement: business must have at least the specified plan tier.
/// </summary>
public class PlanRequirement : IAuthorizationRequirement
{
    public PlanType MinimumPlan { get; }

    public PlanRequirement(PlanType minimumPlan)
    {
        MinimumPlan = minimumPlan;
    }
}

/// <summary>
/// Reads the "planType" claim from the JWT and compares against the required minimum.
/// </summary>
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
