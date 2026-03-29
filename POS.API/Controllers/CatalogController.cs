using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Repository;

namespace POS.API.Controllers;

/// <summary>
/// Read-only system catalogs for dropdowns, setup, and onboarding.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[AllowAnonymous]
public class CatalogController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;

    public CatalogController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    [HttpGet("kitchen-statuses")]
    public async Task<IActionResult> GetKitchenStatuses() =>
        Ok(await _unitOfWork.Catalog.GetKitchenStatusesAsync());

    [HttpGet("display-statuses")]
    public async Task<IActionResult> GetDisplayStatuses() =>
        Ok(await _unitOfWork.Catalog.GetDisplayStatusesAsync());

    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods() =>
        Ok(await _unitOfWork.Catalog.GetPaymentMethodsAsync());

    [HttpGet("device-modes")]
    public async Task<IActionResult> GetDeviceModes() =>
        Ok(await _unitOfWork.Catalog.GetDeviceModesAsync());

    [HttpGet("business-types")]
    public async Task<IActionResult> GetBusinessTypes() =>
        Ok(await _unitOfWork.Catalog.GetBusinessTypesAsync());

    [HttpGet("zone-types")]
    public async Task<IActionResult> GetZoneTypes() =>
        Ok(await _unitOfWork.Catalog.GetZoneTypesAsync());

    [HttpGet("plan-types")]
    public async Task<IActionResult> GetPlanTypes() =>
        Ok(await _unitOfWork.Catalog.GetPlanTypesAsync());
}
