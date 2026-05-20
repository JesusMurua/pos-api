using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using POS.Domain.DTOs.Bridge;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Bridges the Angular frontend with the local hardware bridge (Windows Service).
/// Currently exposes thermal-printer dispatch; future verticals (scales,
/// scanners) will hang off this controller too. Branch tenancy is enforced
/// implicitly via the inherited <c>BranchId</c> property — the request body
/// never carries a branch identifier, so cross-tenant printing is impossible
/// from this endpoint.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class HardwareController : BaseApiController
{
    private readonly IBridgeNotifier _bridgeNotifier;
    private readonly ILogger<HardwareController> _logger;

    public HardwareController(
        IBridgeNotifier bridgeNotifier,
        ILogger<HardwareController> logger)
    {
        _bridgeNotifier = bridgeNotifier;
        _logger = logger;
    }

    /// <summary>
    /// Dispatches a raw ESC/POS byte stream (base64-encoded) to a specific
    /// thermal printer attached to the caller's branch bridge. Fire-and-forget
    /// at the SignalR layer — without a persistent outbox the command is lost
    /// if no bridge is currently connected to the branch group.
    /// </summary>
    /// <param name="payload">Printer identifier plus base64-encoded ESC/POS bytes.</param>
    /// <response code="200">Print command dispatched to the local bridge.</response>
    /// <response code="400">Validation failed (missing PrinterId, empty/oversized payload, malformed base64).</response>
    /// <response code="401">Caller is not authenticated.</response>
    /// <response code="403">Caller lacks the Owner/Manager/Cashier role.</response>
    /// <response code="429">Rate limit exceeded for the caller's IP.</response>
    [HttpPost("print")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [EnableRateLimiting("PrintCommandPolicy")]
    public async Task<IActionResult> Print([FromBody] EscPosPayloadDto payload)
    {
        if (string.IsNullOrWhiteSpace(payload.PrinterId))
            return BadRequest(new { message = "PrinterId is required." });

        if (string.IsNullOrWhiteSpace(payload.Base64Bytes) || payload.Base64Bytes.Length > 512_000)
            return BadRequest(new { message = "Payload too large or empty." });

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload.Base64Bytes);
        }
        catch (FormatException)
        {
            return BadRequest(new { message = "Invalid Base64 payload." });
        }

        // Audit log BEFORE dispatch so the attempt is observable even if the
        // SignalR push throws downstream.
        _logger.LogInformation(
            "Print attempt: branch={BranchId}, printer={PrinterId}, bytes={ByteCount}, user={UserId}",
            BranchId, payload.PrinterId, bytes.Length, UserId);

        await _bridgeNotifier.SendEscPosCommandAsync(BranchId, payload.PrinterId, bytes);

        return Ok(new { success = true, message = "Print command dispatched." });
    }
}
