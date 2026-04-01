using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing delivery aggregator orders (UberEats, Rappi, DidiFood).
/// </summary>
[Route("api/[controller]")]
public class DeliveryController : BaseApiController
{
    private readonly IDeliveryService _deliveryService;
    private readonly IUnitOfWork _unitOfWork;

    public DeliveryController(IDeliveryService deliveryService, IUnitOfWork unitOfWork)
    {
        _deliveryService = deliveryService;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Gets all active delivery orders for the current branch.
    /// </summary>
    /// <returns>List of active delivery orders.</returns>
    /// <response code="200">Returns active delivery orders.</response>
    [HttpGet("active")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen")]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActive()
    {
        var orders = await _deliveryService.GetActiveDeliveryOrdersAsync(BranchId);
        return Ok(orders);
    }

    /// <summary>
    /// Accepts a pending delivery order.
    /// </summary>
    /// <param name="orderId">The order UUID.</param>
    /// <returns>The accepted order.</returns>
    /// <response code="200">Order accepted successfully.</response>
    /// <response code="400">If the order cannot be accepted (wrong status).</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{orderId}/accept")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Accept(string orderId)
    {
        try
        {
            var order = await _deliveryService.AcceptDeliveryOrderAsync(orderId, BranchId);
            return Ok(order);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Rejects a pending delivery order.
    /// </summary>
    /// <param name="orderId">The order UUID.</param>
    /// <param name="request">The rejection reason.</param>
    /// <returns>The rejected order.</returns>
    /// <response code="200">Order rejected successfully.</response>
    /// <response code="400">If the order cannot be rejected (wrong status).</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{orderId}/reject")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reject(string orderId, [FromBody] RejectDeliveryRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var order = await _deliveryService.RejectDeliveryOrderAsync(orderId, request.Reason, BranchId);
            return Ok(order);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Marks a delivery order as ready for courier pickup.
    /// </summary>
    /// <param name="orderId">The order UUID.</param>
    /// <returns>The updated order.</returns>
    /// <response code="200">Order marked ready.</response>
    /// <response code="400">If the order cannot be marked ready (wrong status).</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{orderId}/ready")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ready(string orderId)
    {
        try
        {
            var order = await _deliveryService.MarkReadyForPickupAsync(orderId, BranchId);
            return Ok(order);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Marks a delivery order as picked up by the courier.
    /// </summary>
    /// <param name="orderId">The order UUID.</param>
    /// <returns>The updated order.</returns>
    /// <response code="200">Order marked as picked up.</response>
    /// <response code="400">If the order cannot be marked picked up (wrong status).</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{orderId}/picked-up")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PickedUp(string orderId)
    {
        try
        {
            var order = await _deliveryService.MarkPickedUpAsync(orderId, BranchId);
            return Ok(order);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Webhook endpoint for delivery platforms to push new orders.
    /// Secured via X-Webhook-Secret header validated against per-branch config.
    /// </summary>
    /// <param name="source">The delivery platform (UberEats, Rappi, DidiFood).</param>
    /// <param name="branchId">The target branch identifier.</param>
    /// <param name="request">The delivery order data.</param>
    /// <returns>The created order.</returns>
    /// <response code="200">Order ingested successfully.</response>
    /// <response code="400">If validation fails or duplicate external ID.</response>
    /// <response code="401">If the webhook secret is invalid.</response>
    /// <response code="404">If the platform is not configured for this branch.</response>
    [HttpPost("webhook/{source}/{branchId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Webhook(string source, int branchId, [FromBody] IngestDeliveryOrderRequest request)
    {
        if (!Enum.TryParse<OrderSource>(source, true, out var orderSource) || orderSource == OrderSource.Direct)
            return BadRequest(new { message = $"Invalid delivery source: {source}" });

        var config = await _unitOfWork.BranchDeliveryConfigs
            .GetByBranchAndPlatformAsync(branchId, orderSource);

        if (config == null)
            return NotFound(new { message = $"Platform {source} not configured for this branch." });

        if (!config.IsActive)
            return BadRequest(new { message = $"Platform {source} integration is not active." });

        var providedSecret = Request.Headers["X-Webhook-Secret"].FirstOrDefault();
        if (string.IsNullOrEmpty(config.WebhookSecret) || providedSecret != config.WebhookSecret)
            return Unauthorized(new { message = "Invalid webhook secret." });

        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            request.Source = orderSource;
            var order = await _deliveryService.IngestWebhookOrderAsync(request, branchId);
            return Ok(order);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

/// <summary>
/// Request body for rejecting a delivery order.
/// </summary>
public class RejectDeliveryRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string Reason { get; set; } = null!;
}
