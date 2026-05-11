using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Filters;
using POS.Domain.DTOs.AccessControl;
using POS.Domain.Enums;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Gym/access-control endpoints. Both routes are gated by the
/// <see cref="FeatureKey.RealtimeAccessControl"/> plan × giro feature; the
/// admin-only enrolment endpoint additionally requires Owner or Manager role.
/// </summary>
[Route("api/[controller]")]
[RequiresFeature(FeatureKey.RealtimeAccessControl)]
public class AccessControlController : BaseApiController
{
    private readonly IAccessControlService _accessControlService;

    public AccessControlController(IAccessControlService accessControlService)
    {
        _accessControlService = accessControlService;
    }

    /// <summary>
    /// Bridge endpoint — evaluates a scanned QR token, writes the audit row
    /// (only when a customer matches), pushes an <c>OpenTurnstile</c> event to
    /// the local bridge over SignalR when access is granted, and returns the
    /// decision DTO so the bridge UI can render the customer name.
    /// </summary>
    [HttpPost("scan-qr")]
    [ProducesResponseType(typeof(AccessResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ScanQr([FromBody] ScanQrRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _accessControlService.EvaluateQrAccessAsync(
            request.QrToken, BranchId, BusinessId);

        return Ok(result);
    }

    /// <summary>
    /// Admin endpoint — associates a plain QR token with a customer in the
    /// caller's business. The service HMAC-hashes the token before persistence.
    /// </summary>
    [HttpPost("enroll-qr")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnrollQr([FromBody] EnrollQrRequestDto request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _accessControlService.EnrollQrTokenAsync(
            request.CustomerId, request.QrToken, BusinessId);

        return NoContent();
    }

    /// <summary>
    /// Admin endpoint — returns whether the given customer has an enrolled QR
    /// token. The stored value is an irreversible HMAC hash, so the response
    /// only exposes the boolean enrollment state.
    /// </summary>
    [HttpGet("customers/{customerId}/qr-status")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(QrStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCustomerQrStatus([FromRoute] int customerId)
    {
        var status = await _accessControlService.GetCustomerQrStatusAsync(customerId, BusinessId);
        return Ok(status);
    }

    /// <summary>
    /// Admin endpoint — clears the customer's enrolled QR token. Idempotent.
    /// Emits a structured information log post-commit for forensic traceability.
    /// </summary>
    [HttpDelete("customers/{customerId}/qr")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RevokeCustomerQr([FromRoute] int customerId)
    {
        await _accessControlService.RevokeQrTokenAsync(customerId, BusinessId);
        return NoContent();
    }
}
