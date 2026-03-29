using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly ApplicationDbContext _context;

    public CatalogController(ApplicationDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Returns all kitchen status options with colors.
    /// </summary>
    [HttpGet("kitchen-statuses")]
    public async Task<IActionResult> GetKitchenStatuses()
    {
        var items = await _context.KitchenStatusCatalogs
            .AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        return Ok(items);
    }

    /// <summary>
    /// Returns all display status options with colors.
    /// </summary>
    [HttpGet("display-statuses")]
    public async Task<IActionResult> GetDisplayStatuses()
    {
        var items = await _context.DisplayStatusCatalogs
            .AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        return Ok(items);
    }

    /// <summary>
    /// Returns all payment method options.
    /// </summary>
    [HttpGet("payment-methods")]
    public async Task<IActionResult> GetPaymentMethods()
    {
        var items = await _context.PaymentMethodCatalogs
            .AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        return Ok(items);
    }

    /// <summary>
    /// Returns all device mode options.
    /// </summary>
    [HttpGet("device-modes")]
    public async Task<IActionResult> GetDeviceModes()
    {
        var items = await _context.DeviceModeCatalogs
            .AsNoTracking().ToListAsync();
        return Ok(items);
    }

    /// <summary>
    /// Returns all business type options with HasKitchen/HasTables flags.
    /// </summary>
    [HttpGet("business-types")]
    public async Task<IActionResult> GetBusinessTypes()
    {
        var items = await _context.BusinessTypeCatalogs
            .AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        return Ok(items);
    }

    /// <summary>
    /// Returns all zone type options.
    /// </summary>
    [HttpGet("zone-types")]
    public async Task<IActionResult> GetZoneTypes()
    {
        var items = await _context.ZoneTypeCatalogs
            .AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        return Ok(items);
    }

    /// <summary>
    /// Returns all plan type options.
    /// </summary>
    [HttpGet("plan-types")]
    public async Task<IActionResult> GetPlanTypes()
    {
        var items = await _context.PlanTypeCatalogs
            .AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync();
        return Ok(items);
    }
}
