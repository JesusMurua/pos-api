using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing suppliers.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class SupplierController : BaseApiController
{
    private readonly ISupplierService _supplierService;

    public SupplierController(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    /// <summary>
    /// Gets all active suppliers for the current branch.
    /// </summary>
    /// <returns>A list of suppliers.</returns>
    /// <response code="200">Returns the list of suppliers.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<Supplier>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var suppliers = await _supplierService.GetAllAsync(BranchId);
        return Ok(suppliers);
    }

    /// <summary>
    /// Gets a supplier by its identifier.
    /// </summary>
    /// <param name="id">The supplier identifier.</param>
    /// <returns>The requested supplier.</returns>
    /// <response code="200">Returns the requested supplier.</response>
    /// <response code="404">If the supplier is not found.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Supplier), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var supplier = await _supplierService.GetByIdAsync(id, BranchId);
        return Ok(supplier);
    }

    /// <summary>
    /// Creates a new supplier.
    /// </summary>
    /// <param name="request">The supplier data.</param>
    /// <returns>The created supplier identifier.</returns>
    /// <response code="200">Returns the created supplier identifier.</response>
    /// <response code="400">If the supplier data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateSupplierRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _supplierService.CreateAsync(request, BranchId);
        return Ok(new { id = created.Id });
    }

    /// <summary>
    /// Updates an existing supplier.
    /// </summary>
    /// <param name="id">The supplier identifier.</param>
    /// <param name="request">The updated supplier data.</param>
    /// <returns>The updated supplier.</returns>
    /// <response code="200">Returns the updated supplier.</response>
    /// <response code="404">If the supplier is not found.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Supplier), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateSupplierRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _supplierService.UpdateAsync(id, request, BranchId);
        return Ok(updated);
    }

    /// <summary>
    /// Soft deletes a supplier.
    /// </summary>
    /// <param name="id">The supplier identifier.</param>
    /// <returns>Success status.</returns>
    /// <response code="200">Returns success status.</response>
    /// <response code="404">If the supplier is not found.</response>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _supplierService.DeleteAsync(id, BranchId);
        return Ok(new { success = true });
    }
}
