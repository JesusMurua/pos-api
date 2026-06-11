using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only read access to the SaaS-billing catalogs that back UI selectors: the
/// <c>SaaSBillingMethod</c> rails and the <c>PlanAddOn</c> catalog. Authenticated
/// exclusively via the <c>X-Admin-Token</c> header. Read-only — both catalogs are code-seeded.
/// </summary>
[Route("api/Admin")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminBillingCatalogController : ControllerBase
{
    private readonly IAdminBillingCatalogService _service;

    public AdminBillingCatalogController(IAdminBillingCatalogService service)
    {
        _service = service;
    }

    /// <summary>Every SaaS billing-method rail, ordered by <c>SortOrder</c>.</summary>
    [HttpGet("billing-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<SaaSBillingMethodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBillingMethods() => Ok(await _service.GetBillingMethodsAsync());

    /// <summary>The full add-on catalog (active and inactive).</summary>
    [HttpGet("plan-add-ons")]
    [ProducesResponseType(typeof(IReadOnlyList<PlanAddOnDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPlanAddOns() => Ok(await _service.GetPlanAddOnsAsync());
}
