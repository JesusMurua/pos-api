using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only admin surface for system catalogs. Authenticated exclusively via
/// the <c>X-Admin-Token</c> header (see <see cref="AdminTokenAuthenticationHandler"/>),
/// NOT JWT Bearer — these endpoints exist for deploy pipelines and on-call
/// operators, not end users. Rate limited at 10 requests / minute / IP via
/// the <c>CatalogInvalidatePolicy</c> sliding-window limiter so a runaway
/// script cannot churn the cache.
/// </summary>
[Route("api/Admin/catalogs")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminCatalogController : ControllerBase
{
    /// <summary>
    /// Server-side whitelist mirroring the 11 simple catalog keys served by
    /// <see cref="CatalogController"/>. Stored case-insensitively for input
    /// flexibility but enumerated in canonical PascalCase to feed
    /// <see cref="ICatalogService.Invalidate"/>. Taxes (per-country sub-keys)
    /// is deliberately excluded from v1 — invalidation of country-specific
    /// tax buckets will arrive in a follow-up PR with its own access pattern.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedCatalogKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["KitchenStatuses"] = "KitchenStatuses",
            ["DisplayStatuses"] = "DisplayStatuses",
            ["PaymentMethods"] = "PaymentMethods",
            ["DeviceModes"] = "DeviceModes",
            ["BusinessTypes"] = "BusinessTypes",
            ["MacroCategories"] = "MacroCategories",
            ["ZoneTypes"] = "ZoneTypes",
            ["PlanTypes"] = "PlanTypes",
            ["AccessReasons"] = "AccessReasons",
            ["AccessMethods"] = "AccessMethods",
            ["Plans"] = "Plans",
        };

    private readonly ICatalogService _catalogService;
    private readonly ILogger<AdminCatalogController> _logger;

    public AdminCatalogController(
        ICatalogService catalogService,
        ILogger<AdminCatalogController> logger)
    {
        _catalogService = catalogService;
        _logger = logger;
    }

    /// <summary>
    /// Evicts one or more catalog entries from the in-memory cache so the
    /// next consumer read repopulates from the database. Atomic: if any key
    /// in the payload is unknown the entire request is rejected with 400 and
    /// no eviction occurs. Idempotent: invalidating an already-empty cache
    /// is a no-op.
    /// </summary>
    /// <returns>
    /// <c>204 No Content</c> on success, <c>400 Bad Request</c> if any
    /// supplied key is not in the server-side whitelist (the unknown keys
    /// are returned in the body so the caller can correct and retry),
    /// <c>401 Unauthorized</c> if the <c>X-Admin-Token</c> header is missing
    /// or invalid, <c>429 Too Many Requests</c> if the per-IP sliding window
    /// is exhausted.
    /// </returns>
    [HttpPost("invalidate")]
    [EnableRateLimiting("CatalogInvalidatePolicy")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public IActionResult Invalidate([FromBody] AdminCatalogInvalidateRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        var unknown = request.CatalogKeys
            .Where(k => !AllowedCatalogKeys.ContainsKey(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count > 0)
        {
            // Audit before returning so failed attempts are traceable.
            LogAudit(request.CatalogKeys, "InvalidKeys", stopwatch.ElapsedMilliseconds);
            return BadRequest(new
            {
                error = "Unknown catalog key(s)",
                invalidKeys = unknown,
                allowedKeys = AllowedCatalogKeys.Values
            });
        }

        var canonicalKeys = request.CatalogKeys
            .Select(k => AllowedCatalogKeys[k])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var key in canonicalKeys)
        {
            _catalogService.Invalidate(key);
        }

        LogAudit(canonicalKeys, "Success", stopwatch.ElapsedMilliseconds);
        return NoContent();
    }

    private void LogAudit(IReadOnlyCollection<string> catalogKeys, string result, long durationMs)
    {
        var tokenId = User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value
                      ?? "anonymous";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Catalog invalidate {@CatalogInvalidateAudit}",
            new
            {
                Timestamp = DateTime.UtcNow,
                CallerTokenId = tokenId,
                CallerIp = ip,
                CatalogKeys = catalogKeys,
                Result = result,
                DurationMs = durationMs
            });
    }
}
