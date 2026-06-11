using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only read access to the <c>BusinessAuditLog</c> trail (the explicit operator-action
/// log written since PR-1a). Authenticated exclusively via the <c>X-Admin-Token</c> header.
/// Read-only — the log is append-only and written by the mutating admin services.
/// </summary>
[Route("api/Admin")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminAuditLogController : ControllerBase
{
    private readonly IAdminAuditLogService _service;

    public AdminAuditLogController(IAdminAuditLogService service)
    {
        _service = service;
    }

    /// <summary>Audit rows for one tenant, newest first.</summary>
    [HttpGet("businesses/{businessId:int}/audit-log")]
    [ProducesResponseType(typeof(PagedBusinessAuditLogDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForBusiness(
        int businessId,
        [FromQuery] string? action, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.GetForBusinessAsync(businessId, action, from, to, page, pageSize));

    /// <summary>Cross-tenant audit rows, newest first. Optionally narrowed by <c>businessId</c>.</summary>
    [HttpGet("audit-log")]
    [ProducesResponseType(typeof(PagedBusinessAuditLogDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCrossTenant(
        [FromQuery] int? businessId, [FromQuery] string? action,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _service.GetCrossTenantAsync(businessId, action, from, to, page, pageSize));
}
