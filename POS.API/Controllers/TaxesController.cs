using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Extensions;
using POS.Domain.DTOs.Tax;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Read-only access to the <c>Tax</c> catalog. Backs the per-product tax
/// selector and any UI that needs to render rates/labels without knowing
/// the catalog's internal ids.
/// <para>
/// Route (<c>/api/Taxes</c>) and <see cref="AuthorizeAttribute"/> posture
/// are preserved unchanged from production. The refactor in BDD-021 wires
/// this action through the same uniform cache-aside + ETag negotiation
/// pattern used by <c>CatalogController</c>.
/// </para>
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class TaxesController : BaseApiController
{
    private readonly ICatalogService _catalogService;

    public TaxesController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    /// <summary>
    /// Returns every tax row in the catalog, optionally filtered by ISO 3166-1
    /// alpha-2 country code (e.g. <c>MX</c>). Results are ordered by country,
    /// with the country's <c>IsDefault</c> row first, then by rate ascending.
    /// </summary>
    /// <param name="countryCode">Optional ISO 3166-1 alpha-2 country filter.</param>
    /// <returns>200 with the matching catalog rows, or 304 when the client's ETag is still valid.</returns>
    /// <response code="200">Returns the matching catalog entries.</response>
    /// <response code="304">Client's cached representation is still current (ETag matched).</response>
    /// <response code="400">When <paramref name="countryCode"/> is supplied and does not match VR-001.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TaxDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTaxes([FromQuery] string? countryCode = null)
    {
        if (!IsCountryCodeValid(countryCode))
        {
            return BadRequest(new
            {
                error = "validation_error",
                message = "El parámetro 'countryCode' debe ser un código ISO 3166-1 alpha-2 de 2 letras.",
                statusCode = 400
            });
        }

        var envelope = await _catalogService.GetTaxCatalogAsync(countryCode);
        return this.ETagResult(envelope);
    }

    /// <summary>
    /// VR-001 (BDD-021 §6.2). When supplied, <paramref name="countryCode"/>
    /// must be exactly two alphabetic characters (ISO 3166-1 alpha-2).
    /// Whitespace-only or empty values are treated as "not supplied" and
    /// pass validation — the service then returns every tax row.
    /// </summary>
    private static bool IsCountryCodeValid(string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return true;
        }

        var trimmed = countryCode.Trim();
        if (trimmed.Length != 2) return false;

        foreach (var ch in trimmed)
        {
            if (!char.IsLetter(ch)) return false;
        }

        return true;
    }
}
