using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only admin surface over a tenant's SaaS subscription: view + reconcile
/// (plan/price/rail/CFDI). Stripe-rail price changes reconcile remote-first.
/// Authenticated via <c>X-Admin-Token</c>. See docs/saas-billing-architecture.md §5/§7.
/// </summary>
[Route("api/Admin/businesses/{businessId:int}/subscription")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminSubscriptionController : ControllerBase
{
    private readonly IAdminSubscriptionService _service;

    public AdminSubscriptionController(IAdminSubscriptionService service)
    {
        _service = service;
    }

    private string? AdminTokenId =>
        User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value;

    /// <summary>Subscription detail + price history.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminSubscriptionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int businessId) => Ok(await _service.GetAsync(businessId));

    /// <summary>Provision a subscription where none exists (Stripe rail: remote-first).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(AdminSubscriptionDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Create(int businessId, [FromBody] AdminCreateSubscriptionRequest request)
    {
        var detail = await _service.CreateAsync(businessId, request, AdminTokenId);
        return CreatedAtAction(nameof(Get), new { businessId }, detail);
    }

    /// <summary>Reconcile the subscription (remote-first on the Stripe rail).</summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int businessId, [FromBody] AdminUpdateSubscriptionRequest request)
    {
        await _service.UpdateAsync(businessId, request, AdminTokenId);
        return NoContent();
    }

    /// <summary>Activate an add-on (remote-first on the Stripe rail).</summary>
    [HttpPost("add-ons")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ActivateAddOn(int businessId, [FromBody] AdminActivateAddOnRequest request)
    {
        await _service.ActivateAddOnAsync(businessId, request, AdminTokenId);
        return NoContent();
    }

    /// <summary>Deactivate an active add-on (soft; archives a custom Stripe Price).</summary>
    [HttpDelete("add-ons/{subscriptionAddOnId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateAddOn(int businessId, int subscriptionAddOnId)
    {
        await _service.DeactivateAddOnAsync(businessId, subscriptionAddOnId, AdminTokenId);
        return NoContent();
    }
}
