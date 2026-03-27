using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing discount presets.
/// </summary>
[Route("api/[controller]")]
public class DiscountPresetController : BaseApiController
{
    private readonly IDiscountPresetService _discountPresetService;

    public DiscountPresetController(IDiscountPresetService discountPresetService)
    {
        _discountPresetService = discountPresetService;
    }

    /// <summary>
    /// Gets all active discount presets for the current branch.
    /// </summary>
    /// <returns>A list of active discount presets.</returns>
    /// <response code="200">Returns the list of discount presets.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<DiscountPreset>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch()
    {
        var presets = await _discountPresetService.GetByBranchAsync(BranchId);
        return Ok(presets);
    }

    /// <summary>
    /// Creates a new discount preset.
    /// </summary>
    /// <param name="preset">The discount preset data.</param>
    /// <returns>The ID of the created preset.</returns>
    /// <response code="200">Returns the ID of the created preset.</response>
    /// <response code="400">If the preset data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] DiscountPreset preset)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _discountPresetService.CreateAsync(preset);
        return Ok(new { id = created.Id });
    }

    /// <summary>
    /// Updates a discount preset.
    /// </summary>
    /// <param name="id">The preset identifier.</param>
    /// <param name="preset">The updated preset data.</param>
    /// <returns>The updated discount preset.</returns>
    /// <response code="200">Returns the updated preset.</response>
    /// <response code="404">If the preset is not found.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(DiscountPreset), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] DiscountPreset preset)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _discountPresetService.UpdateAsync(id, preset);
        return Ok(updated);
    }

    /// <summary>
    /// Soft deletes a discount preset.
    /// </summary>
    /// <param name="id">The preset identifier.</param>
    /// <returns>Success confirmation.</returns>
    /// <response code="200">Preset successfully deactivated.</response>
    /// <response code="404">If the preset is not found.</response>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _discountPresetService.DeleteAsync(id);
        return Ok(new { success = true });
    }
}
