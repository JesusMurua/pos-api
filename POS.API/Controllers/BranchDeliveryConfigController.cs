using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing delivery platform configurations per branch.
/// </summary>
[Route("api/branch/{branchId}/delivery-config")]
public class BranchDeliveryConfigController : BaseApiController
{
    private readonly IBranchDeliveryConfigService _configService;

    public BranchDeliveryConfigController(IBranchDeliveryConfigService configService)
    {
        _configService = configService;
    }

    /// <summary>
    /// Gets all delivery platform configs for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier (must match JWT).</param>
    /// <returns>List of delivery platform configurations.</returns>
    /// <response code="200">Returns the list of configs.</response>
    [HttpGet]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(IEnumerable<BranchDeliveryConfigDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch(int branchId)
    {
        var configs = await _configService.GetByBranchAsync(branchId, GetBaseUrl());
        return Ok(configs);
    }

    /// <summary>
    /// Creates or updates a delivery platform config for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier (must match JWT).</param>
    /// <param name="request">The platform configuration data.</param>
    /// <returns>The upserted config as DTO.</returns>
    /// <response code="200">Returns the upserted config.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPut]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(BranchDeliveryConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Upsert(int branchId, [FromBody] UpsertDeliveryConfigRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var dto = await _configService.UpsertAsync(branchId, request, GetBaseUrl());
            return Ok(dto);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Deletes a delivery platform config for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier (must match JWT).</param>
    /// <param name="platform">The platform to remove (UberEats, Rappi, DidiFood).</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Config deleted.</response>
    /// <response code="400">If the platform is invalid.</response>
    /// <response code="404">If the config is not found.</response>
    [HttpDelete("{platform}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int branchId, string platform)
    {
        if (!Enum.TryParse<OrderSource>(platform, true, out var parsedPlatform)
            || parsedPlatform == OrderSource.Direct)
            return BadRequest(new { message = $"Invalid platform: {platform}" });

        try
        {
            await _configService.DeleteAsync(branchId, parsedPlatform);
            return Ok(new { message = "Delivery config deleted" });
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    private string GetBaseUrl() =>
        $"{Request.Scheme}://{Request.Host}";
}
