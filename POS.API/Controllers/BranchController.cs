using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Models;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing branch configuration and PIN.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Owner")]
public class BranchController : ControllerBase
{
    private readonly IBranchService _branchService;

    public BranchController(IBranchService branchService)
    {
        _branchService = branchService;
    }

    /// <summary>
    /// Retrieves branch configuration with business data.
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <returns>The branch configuration.</returns>
    /// <response code="200">Returns the branch configuration.</response>
    /// <response code="404">If the branch is not found.</response>
    [HttpGet("{id}/config")]
    [ProducesResponseType(typeof(Branch), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfig(int id)
    {
        var branch = await _branchService.GetConfigAsync(id);
        return Ok(branch);
    }

    /// <summary>
    /// Updates the branch name and location.
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <param name="request">The update data.</param>
    /// <returns>The updated branch.</returns>
    /// <response code="200">Returns the updated branch.</response>
    /// <response code="404">If the branch is not found.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPut("{id}/config")]
    [ProducesResponseType(typeof(Branch), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateConfig(int id, [FromBody] UpdateConfigRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var branch = await _branchService.UpdateConfigAsync(id, request.Name, request.LocationName);
        return Ok(branch);
    }

    /// <summary>
    /// Verifies a PIN against the branch's stored hash.
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <param name="request">The PIN to verify.</param>
    /// <returns>Whether the PIN is valid.</returns>
    /// <response code="200">Returns the verification result.</response>
    /// <response code="404">If the branch is not found.</response>
    [HttpPost("{id}/verify-pin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyPin(int id, [FromBody] VerifyPinRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var isValid = await _branchService.VerifyPinAsync(id, request.Pin);
        return Ok(new { isValid });
    }

    /// <summary>
    /// Updates the branch PIN after verifying the current one.
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <param name="request">The current and new PIN.</param>
    /// <returns>Success status.</returns>
    /// <response code="200">PIN updated successfully.</response>
    /// <response code="400">If the current PIN is incorrect.</response>
    /// <response code="404">If the branch is not found.</response>
    [HttpPut("{id}/pin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePin(int id, [FromBody] UpdatePinRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _branchService.UpdatePinAsync(id, request.CurrentPin, request.NewPin);
        return Ok(new { message = "PIN updated successfully" });
    }
}
