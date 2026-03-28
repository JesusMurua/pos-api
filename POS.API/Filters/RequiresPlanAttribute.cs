using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using POS.Domain.Enums;

namespace POS.API.Filters;

/// <summary>
/// Action filter that verifies the business plan meets the minimum required level.
/// Returns HTTP 402 if the current plan is below the required plan.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequiresPlanAttribute : Attribute, IAuthorizationFilter
{
    private readonly PlanType _minimumPlan;

    public RequiresPlanAttribute(PlanType minimumPlan)
    {
        _minimumPlan = minimumPlan;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var planClaim = context.HttpContext.User.FindFirst("planType");
        if (planClaim == null || !Enum.TryParse<PlanType>(planClaim.Value, out var currentPlan))
        {
            context.Result = new ObjectResult(new
            {
                error = "plan_required",
                message = $"Esta función requiere Plan {_minimumPlan}",
                requiredPlan = _minimumPlan.ToString(),
                currentPlan = "Unknown"
            })
            { StatusCode = 402 };
            return;
        }

        if (currentPlan < _minimumPlan)
        {
            context.Result = new ObjectResult(new
            {
                error = "plan_required",
                message = $"Esta función requiere Plan {_minimumPlan}",
                requiredPlan = _minimumPlan.ToString(),
                currentPlan = currentPlan.ToString()
            })
            { StatusCode = 402 };
        }
    }
}
