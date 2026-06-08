using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Repository;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only views over orders across all tenants. Authenticated exclusively via
/// the <c>X-Admin-Token</c> header (never the end-user JWT).
/// </summary>
[Route("api/Admin")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminOrdersController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminOrdersController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Drift report: payments synced with an unknown method (recorded as Other)
    /// or a method not authorized by the tenant's plan. Cross-tenant, paginated.
    /// </summary>
    [HttpGet("orders/unauthorized-methods")]
    [ProducesResponseType(typeof(PagedDriftReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnauthorizedMethods(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var toUtc = (to ?? DateTime.UtcNow).ToUniversalTime();
        var fromUtc = (from ?? toUtc.AddDays(-30)).ToUniversalTime();
        pageSize = Math.Clamp(pageSize, 1, 200);

        var result = await _unitOfWork.Orders.GetFlaggedPaymentsAsync(fromUtc, toUtc, page, pageSize);
        return Ok(result);
    }
}
