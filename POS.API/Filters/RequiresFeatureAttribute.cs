using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Services.IService;

namespace POS.API.Filters;

/// <summary>
/// Action filter that verifies a feature is enabled for the caller's business
/// via the Plan × BusinessType matrix. Returns HTTP 402 Payment Required when
/// the feature is unavailable under the current plan or giro.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequiresFeatureAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly FeatureKey _feature;

    public RequiresFeatureAttribute(FeatureKey feature)
    {
        _feature = feature;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var businessIdClaim = context.HttpContext.User.FindFirst("businessId")?.Value;
        if (!int.TryParse(businessIdClaim, out var businessId))
        {
            context.Result = BuildPaymentRequired(_feature, "Unknown", null);
            return;
        }

        var gate = context.HttpContext.RequestServices.GetRequiredService<IFeatureGateService>();

        try
        {
            await gate.EnforceAsync(businessId, _feature);
        }
        catch (PlanLimitExceededException ex)
        {
            context.Result = BuildPaymentRequired(_feature, ex.CurrentPlan, ex.Limit);
        }
    }

    private static ObjectResult BuildPaymentRequired(FeatureKey feature, string currentPlan, int? limit)
    {
        var payload = new
        {
            error = "feature_not_available",
            message = $"La función '{feature}' no está disponible en tu plan actual.",
            feature = feature.ToString(),
            currentPlan,
            limit,
            statusCode = 402
        };

        return new ObjectResult(payload) { StatusCode = StatusCodes.Status402PaymentRequired };
    }
}
