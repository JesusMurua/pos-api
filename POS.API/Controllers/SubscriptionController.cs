using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Models;
using POS.Domain.Exceptions;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Manages subscription status, checkout, and cancellation for the authenticated business.
/// </summary>
[Route("api/[controller]")]
public class SubscriptionController : BaseApiController
{
    private readonly IStripeService _stripeService;
    private readonly ISubscriptionService _subscriptionService;

    public SubscriptionController(IStripeService stripeService, ISubscriptionService subscriptionService)
    {
        _stripeService = stripeService;
        _subscriptionService = subscriptionService;
    }

    /// <summary>
    /// Returns the current subscription status for the authenticated business.
    /// Always returns a valid response — Free plan defaults when no Stripe subscription exists.
    /// </summary>
    [HttpGet("status")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var status = await _subscriptionService.GetStatusAsync(BusinessId);
            return Ok(status);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Creates a Stripe Checkout session and returns the redirect URL.
    /// </summary>
    [HttpPost("checkout")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var url = await _stripeService.CreateCheckoutSessionAsync(
                BusinessId, request.PriceId, request.SuccessUrl, request.CancelUrl);

            return Ok(new { url });
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Cancels the subscription at the end of the current billing period.
    /// </summary>
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel()
    {
        try
        {
            await _stripeService.CancelSubscriptionAsync(BusinessId);
            return Ok(new { message = "Subscription will be canceled at period end" });
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
