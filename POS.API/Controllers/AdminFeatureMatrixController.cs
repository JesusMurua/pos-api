using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only CRUD for the feature matrices (Plan / Macro / Cluster), overrides and
/// feature-catalog metadata. Authenticated exclusively via the <c>X-Admin-Token</c>
/// header — never exposed to the end-user JWT scheme. Every mutation is audited
/// (attributed to the token's <c>token_id</c>) and invalidates the feature cache.
/// </summary>
[Route("api/Admin")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminFeatureMatrixController : ControllerBase
{
    private readonly IFeatureMatrixAdminService _service;

    public AdminFeatureMatrixController(IFeatureMatrixAdminService service)
    {
        _service = service;
    }

    private string? TokenId => User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value;

    // ── Feature catalog (metadata only) ───────────────────────────────

    [HttpGet("feature-catalog")]
    public async Task<IActionResult> GetFeatureCatalog() => Ok(await _service.GetFeatureCatalogAsync());

    [HttpPut("feature-catalog/{id:int}")]
    public async Task<IActionResult> UpdateFeatureMetadata(int id, [FromBody] UpdateFeatureCatalogMetadataRequest request)
    {
        await _service.UpdateFeatureMetadataAsync(id, request, TokenId);
        return NoContent();
    }

    // ── Plan × Feature ────────────────────────────────────────────────

    [HttpGet("plan-feature-matrix")]
    public async Task<IActionResult> GetPlanMatrix() => Ok(await _service.GetPlanMatrixAsync());

    [HttpPut("plan-feature-matrix")]
    public async Task<IActionResult> PutPlanMatrix([FromBody] List<PlanFeatureEntryDto> entries)
    {
        await _service.BulkUpsertPlanMatrixAsync(entries ?? new(), TokenId);
        return NoContent();
    }

    // ── Macro × Feature ───────────────────────────────────────────────

    [HttpGet("business-type-feature-matrix")]
    public async Task<IActionResult> GetMacroMatrix() => Ok(await _service.GetMacroMatrixAsync());

    [HttpPut("business-type-feature-matrix")]
    public async Task<IActionResult> PutMacroMatrix([FromBody] List<MacroFeatureEntryDto> entries)
    {
        await _service.BulkUpsertMacroMatrixAsync(entries ?? new(), TokenId);
        return NoContent();
    }

    // ── Cluster × Feature ─────────────────────────────────────────────

    [HttpGet("cluster-feature-matrix")]
    public async Task<IActionResult> GetClusterMatrix() => Ok(await _service.GetClusterMatrixAsync());

    [HttpPut("cluster-feature-matrix")]
    public async Task<IActionResult> PutClusterMatrix([FromBody] List<ClusterFeatureEntryDto> entries)
    {
        await _service.BulkUpsertClusterMatrixAsync(entries ?? new(), TokenId);
        return NoContent();
    }

    // ── Overrides ─────────────────────────────────────────────────────

    [HttpGet("plan-business-type-overrides")]
    public async Task<IActionResult> GetOverrides() => Ok(await _service.GetOverridesAsync());

    [HttpPost("plan-business-type-overrides")]
    public async Task<IActionResult> CreateOverride([FromBody] OverrideDto dto)
    {
        await _service.CreateOverrideAsync(dto, TokenId);
        return NoContent();
    }

    [HttpPut("plan-business-type-overrides")]
    public async Task<IActionResult> UpdateOverride([FromBody] OverrideDto dto)
    {
        await _service.UpdateOverrideAsync(dto, TokenId);
        return NoContent();
    }

    [HttpDelete("plan-business-type-overrides/{planTypeId:int}/{macroCategoryId:int}/{featureId:int}")]
    public async Task<IActionResult> DeleteOverride(int planTypeId, int macroCategoryId, int featureId)
    {
        await _service.DeleteOverrideAsync(planTypeId, macroCategoryId, featureId, TokenId);
        return NoContent();
    }

    // ── Preview + audit ───────────────────────────────────────────────

    [HttpGet("feature-matrix/preview-impact")]
    public async Task<IActionResult> PreviewImpact(
        [FromQuery] string axis, [FromQuery] string clusterCode,
        [FromQuery] int featureId, [FromQuery] bool isApplicable)
    {
        if (!string.Equals(axis, "cluster", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "unsupported_axis", message = "Only axis=cluster is supported." });

        return Ok(await _service.PreviewClusterImpactAsync(clusterCode, featureId, isApplicable));
    }

    [HttpGet("feature-matrix/audit-log")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? axis, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        return Ok(await _service.GetAuditLogAsync(from, to, axis, page, pageSize));
    }
}
