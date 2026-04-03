using Microsoft.AspNetCore.Authorization;
using POS.Domain.Enums;

namespace POS.API.Authorization;

/// <summary>
/// Requires the authenticated user's business to have at least the specified plan tier.
/// Plan hierarchy: Free(0) &lt; Basic(1) &lt; Pro(2) &lt; Enterprise(3).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequiresPlanAttribute : AuthorizeAttribute
{
    public RequiresPlanAttribute(PlanType minimumPlan)
    {
        Policy = $"RequiresPlan_{minimumPlan}";
    }
}
