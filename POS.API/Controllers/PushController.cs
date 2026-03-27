using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using POS.Domain.Settings;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing push notification subscriptions.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class PushController : BaseApiController
{
    private readonly IPushNotificationService _pushService;
    private readonly VapidSettings _vapidSettings;

    public PushController(
        IPushNotificationService pushService,
        IOptions<VapidSettings> vapidSettings)
    {
        _pushService = pushService;
        _vapidSettings = vapidSettings.Value;
    }

    /// <summary>
    /// Returns the VAPID public key for client-side subscription.
    /// </summary>
    /// <returns>The VAPID public key string.</returns>
    /// <response code="200">Returns the public key.</response>
    [HttpGet("vapid-public-key")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult GetVapidPublicKey()
    {
        return Ok(new { publicKey = _vapidSettings.PublicKey });
    }

    /// <summary>
    /// Saves a push subscription for the authenticated user.
    /// </summary>
    /// <param name="dto">The push subscription data from the browser.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Subscription saved successfully.</response>
    /// <response code="400">If the subscription data is invalid.</response>
    [HttpPost("subscribe")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscriptionDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _pushService.SaveSubscriptionAsync(UserId, BranchId, dto);
        return Ok(new { message = "Subscription saved" });
    }

    /// <summary>
    /// Removes a push subscription by its endpoint.
    /// </summary>
    /// <param name="dto">The endpoint to unsubscribe.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Subscription removed successfully.</response>
    [HttpDelete("unsubscribe")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unsubscribe([FromBody] UnsubscribeRequest dto)
    {
        await _pushService.RemoveSubscriptionAsync(dto.Endpoint);
        return Ok(new { message = "Subscription removed" });
    }
}

/// <summary>
/// Request body for unsubscribing from push notifications.
/// </summary>
public class UnsubscribeRequest
{
    public string Endpoint { get; set; } = null!;
}
