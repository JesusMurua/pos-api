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

    public SubscriptionController(IStripeService stripeService)
    {
        _stripeService = stripeService;
    }

    /// <summary>
    /// Returns the current subscription for the authenticated business.
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var subscription = await _stripeService.GetSubscriptionStatusAsync(BusinessId);
            if (subscription == null)
                return NotFound(new { message = "No subscription found" });

            return Ok(new
            {
                subscription.PlanType,
                subscription.Status,
                subscription.TrialEndsAt,
                subscription.CurrentPeriodEnd,
                subscription.BillingCycle,
                subscription.PricingGroup
            });
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
