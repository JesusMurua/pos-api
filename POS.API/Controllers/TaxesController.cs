using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.DTOs.Tax;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Read-only access to the <c>Tax</c> catalog. Backs the per-product tax
/// selector and any UI that needs to render rates/labels without knowing
/// the catalog's internal ids.
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
    /// <returns>The list of <see cref="TaxDto"/> matching the filter.</returns>
    /// <response code="200">Returns the matching catalog entries.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<TaxDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<TaxDto>>> GetTaxes([FromQuery] string? countryCode = null)
    {
        var taxes = await _catalogService.GetTaxCatalogAsync(countryCode);
        return Ok(taxes);
    }
}
