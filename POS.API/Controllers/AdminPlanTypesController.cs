using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Domain.DTOs.Catalogs;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only read + edit of the plan catalog (price/name/order/currency).
/// Authenticated via the <c>X-Admin-Token</c> header. The price is admin-owned and
/// durable — the boot reseed no longer overwrites <c>MonthlyPrice</c> (OQ-3). Edits
/// invalidate both the <c>PlanTypes</c> and <c>Plans</c> public catalog envelopes.
/// </summary>
[Route("api/Admin/plan-types")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
[EnableRateLimiting("CatalogInvalidatePolicy")]
public class AdminPlanTypesController : ControllerBase
{
    private readonly IPlanTypeAdminService _service;

    public AdminPlanTypesController(IPlanTypeAdminService service)
    {
        _service = service;
    }

    /// <summary>Every plan-type row with its current price (admin view).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PlanTypeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll() => Ok(await _service.GetAllAsync());

    /// <summary>Full-replace of a plan's editable fields (Code/Id immutable).</summary>
    /// <response code="204">Updated; PlanTypes + Plans caches invalidated.</response>
    /// <response code="400">Invalid input (e.g. negative MonthlyPrice).</response>
    /// <response code="404">Plan type not found.</response>
    [HttpPut("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] AdminUpdatePlanTypeRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        await _service.UpdateAsync(id, request);
        return NoContent();
    }
}
