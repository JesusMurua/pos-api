using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Extensions;
using POS.Domain.DTOs.Catalogs;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Read-only system catalogs for dropdowns, setup, and onboarding flows.
/// Every action returns a DTO list inside a <see cref="CatalogResponse{T}"/>
/// envelope which the <see cref="CatalogResponseExtensions.ETagResult{T}"/>
/// helper unwraps into either <c>200 OK</c> or <c>304 Not Modified</c>,
/// always emitting <c>ETag</c> and <c>Cache-Control</c> headers — see
/// BDD-021 §5.1 / §6.1.
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

    #region Workflow Catalogs (Kitchen / Display / Payment / Device Modes)

    /// <summary>Returns the kitchen-status catalog (KDS lifecycle states).</summary>
    [HttpGet("kitchen-statuses")]
    [ProducesResponseType(typeof(IReadOnlyList<KitchenStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetKitchenStatuses() =>
        this.ETagResult(await _catalogService.GetKitchenStatusesAsync());

    /// <summary>Returns the display-status catalog (table-map / front-of-house states).</summary>
    [HttpGet("display-statuses")]
    [ProducesResponseType(typeof(IReadOnlyList<DisplayStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetDisplayStatuses() =>
        this.ETagResult(await _catalogService.GetDisplayStatusesAsync());

    /// <summary>Returns the payment-method catalog (cash, card, transfer, other).</summary>
    [HttpGet("payment-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetPaymentMethods() =>
        this.ETagResult(await _catalogService.GetPaymentMethodsAsync());

    /// <summary>Returns the device-mode catalog (cashier, kiosk, kitchen, …).</summary>
    [HttpGet("device-modes")]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceModeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetDeviceModes() =>
        this.ETagResult(await _catalogService.GetDeviceModesAsync());

    #endregion

    #region Tenant Taxonomy (Business Types / Macro Categories)

    /// <summary>Returns the business sub-giro catalog (e.g. Restaurante, Cafetería).</summary>
    [HttpGet("business-types")]
    [ProducesResponseType(typeof(IReadOnlyList<BusinessTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetBusinessTypes() =>
        this.ETagResult(await _catalogService.GetBusinessTypesAsync());

    /// <summary>
    /// Returns the macro-category catalog with its <c>posExperience</c>,
    /// <c>hasKitchen</c>, and <c>hasTables</c> flags. Source of truth for
    /// the Metadata-Driven Forms described in BDD-020.
    /// </summary>
    [HttpGet("macro-categories")]
    [ProducesResponseType(typeof(IReadOnlyList<MacroCategoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetMacroCategories() =>
        this.ETagResult(await _catalogService.GetMacroCategoriesAsync());

    #endregion

    #region Layout Catalogs (Zone Types)

    /// <summary>Returns the zone-type catalog (Salón, Barra, Otro).</summary>
    [HttpGet("zone-types")]
    [ProducesResponseType(typeof(IReadOnlyList<ZoneTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetZoneTypes() =>
        this.ETagResult(await _catalogService.GetZoneTypesAsync());

    #endregion

    #region Subscription Catalogs (Plan Types / Plan × Feature Matrix)

    /// <summary>Returns the flat subscription-plan catalog.</summary>
    [HttpGet("plan-types")]
    [ProducesResponseType(typeof(IReadOnlyList<PlanTypeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetPlanTypes() =>
        this.ETagResult(await _catalogService.GetPlanTypesAsync());

    /// <summary>
    /// Returns every subscription plan with the list of features it enables.
    /// Single source of truth for the Frontend — replaces any hardcoded
    /// PLAN_CATALOG on the client. Feature <c>Code</c> values match the
    /// JWT <c>features</c> claim verbatim.
    /// </summary>
    [HttpGet("plans")]
    [ProducesResponseType(typeof(IReadOnlyList<PlanCatalogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetPlanCatalog() =>
        this.ETagResult(await _catalogService.GetPlanCatalogAsync());

    #endregion

    #region Access-Control Catalogs (Access Reasons / Methods)

    /// <summary>Returns the access-reason catalog (gym / wellness access control).</summary>
    [HttpGet("access-reasons")]
    [ProducesResponseType(typeof(IReadOnlyList<AccessReasonDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetAccessReasons() =>
        this.ETagResult(await _catalogService.GetAccessReasonsAsync());

    /// <summary>Returns the access-method catalog (QR, biometric, manual).</summary>
    [HttpGet("access-methods")]
    [ProducesResponseType(typeof(IReadOnlyList<AccessMethodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetAccessMethods() =>
        this.ETagResult(await _catalogService.GetAccessMethodsAsync());

    #endregion
}
