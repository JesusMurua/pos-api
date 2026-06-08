using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only CRUD for the payment-method catalog, plan matrix and tenant overrides.
/// Authenticated exclusively via the <c>X-Admin-Token</c> header. Every mutation is
/// audited (by token_id) and invalidates the payment caches.
/// </summary>
[Route("api/Admin")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class PaymentMatrixAdminController : ControllerBase
{
    private readonly IPaymentMatrixAdminService _service;

    public PaymentMatrixAdminController(IPaymentMatrixAdminService service)
    {
        _service = service;
    }

    private string? TokenId => User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value;

    // ── Catalog ───────────────────────────────────────────────────────

    [HttpGet("payment-method-catalog")]
    public async Task<IActionResult> GetCatalog() => Ok(await _service.GetCatalogAsync());

    [HttpPost("payment-method-catalog")]
    public async Task<IActionResult> CreateCatalog([FromBody] UpsertPaymentMethodCatalogRequest request) =>
        Ok(await _service.CreateCatalogAsync(request, TokenId));

    [HttpPut("payment-method-catalog/{id:int}")]
    public async Task<IActionResult> UpdateCatalog(int id, [FromBody] UpsertPaymentMethodCatalogRequest request)
    {
        await _service.UpdateCatalogAsync(id, request, TokenId);
        return NoContent();
    }

    [HttpDelete("payment-method-catalog/{id:int}")]
    public async Task<IActionResult> DeleteCatalog(int id)
    {
        await _service.DeleteCatalogAsync(id, TokenId);
        return NoContent();
    }

    // ── Plan matrix ───────────────────────────────────────────────────

    [HttpGet("plan-payment-method-matrix")]
    public async Task<IActionResult> GetPlanMatrix() => Ok(await _service.GetPlanMatrixAsync());

    [HttpPut("plan-payment-method-matrix")]
    public async Task<IActionResult> BulkUpsertPlanMatrix([FromBody] List<PlanPaymentMethodEntryDto> entries)
    {
        await _service.BulkUpsertPlanMatrixAsync(entries, TokenId);
        return NoContent();
    }

    // ── Tenant overrides ──────────────────────────────────────────────

    [HttpGet("tenant-payment-method-overrides")]
    public async Task<IActionResult> GetOverrides() => Ok(await _service.GetOverridesAsync());

    [HttpPost("tenant-payment-method-overrides")]
    public async Task<IActionResult> CreateOverride([FromBody] CreateTenantOverrideRequest request) =>
        Ok(await _service.CreateOverrideAsync(request, TokenId));

    [HttpPut("tenant-payment-method-overrides/{id:int}")]
    public async Task<IActionResult> UpdateOverride(int id, [FromBody] UpdateTenantOverrideRequest request)
    {
        await _service.UpdateOverrideAsync(id, request, TokenId);
        return NoContent();
    }

    [HttpDelete("tenant-payment-method-overrides/{id:int}")]
    public async Task<IActionResult> DeleteOverride(int id)
    {
        await _service.DeleteOverrideAsync(id, TokenId);
        return NoContent();
    }

    // ── Preview + audit ───────────────────────────────────────────────

    [HttpGet("payment-matrix/preview-impact")]
    public async Task<IActionResult> PreviewImpact(
        [FromQuery] int paymentMethodId, [FromQuery] int planTypeId, [FromQuery] bool enabled) =>
        Ok(await _service.PreviewImpactAsync(paymentMethodId, planTypeId, enabled));

    [HttpGet("payment-matrix/audit-log")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string? axis = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) =>
        Ok(await _service.GetAuditLogAsync(from, to, axis, page, pageSize));
}
