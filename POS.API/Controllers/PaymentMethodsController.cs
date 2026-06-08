using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.DTOs.Catalogs;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Tenant-facing payment methods: the methods the logged-in business may use,
/// after plan + override + country gating. Distinct from the anonymous raw catalog
/// at <c>GET /api/Catalog/payment-methods</c>.
/// </summary>
[Route("api/payment-methods")]
[ApiController]
[Authorize]
public class PaymentMethodsController : BaseApiController
{
    private readonly IPaymentMethodAvailabilityService _service;

    public PaymentMethodsController(IPaymentMethodAvailabilityService service)
    {
        _service = service;
    }

    /// <summary>Payment methods enabled for the current tenant.</summary>
    [HttpGet("available")]
    [ProducesResponseType(typeof(IReadOnlyList<AvailablePaymentMethodDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailable() =>
        Ok(await _service.GetAvailableAsync(BusinessId));
}
