using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Read-only system catalogs for dropdowns, setup, and onboarding.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[AllowAnonymous]
public class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public CatalogController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet("kitchen-statuses")]
    public async Task<IActionResult> GetKitchenStatuses() =>
        Ok(await _catalogService.GetKitchenStatusesAsync());

    [HttpGet("display-statuses")]
    public async Task<IActionResult> GetDisplayStatuses() =>
        Ok(await _catalogService.GetDisplayStatusesAsync());

    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods() =>
        Ok(await _catalogService.GetPaymentMethodsAsync());

    [HttpGet("device-modes")]
    public async Task<IActionResult> GetDeviceModes() =>
        Ok(await _catalogService.GetDeviceModesAsync());

    [HttpGet("business-types")]
    public async Task<IActionResult> GetBusinessTypes() =>
        Ok(await _catalogService.GetBusinessTypesAsync());

    [HttpGet("zone-types")]
    public async Task<IActionResult> GetZoneTypes() =>
        Ok(await _catalogService.GetZoneTypesAsync());

    [HttpGet("plan-types")]
    public async Task<IActionResult> GetPlanTypes() =>
        Ok(await _catalogService.GetPlanTypesAsync());
}
