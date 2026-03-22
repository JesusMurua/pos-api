using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing categories.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Owner")]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryService _categoryService;

    public CategoriesController(ICategoryService categoryService)
    {
        _categoryService = categoryService;
    }

    /// <summary>
    /// Retrieves all active categories for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>A list of active categories.</returns>
    /// <response code="200">Returns the list of active categories.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<Category>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int branchId)
    {
        var categories = await _categoryService.GetAllActiveAsync(branchId);
        return Ok(categories);
    }

    /// <summary>
    /// Creates a new category.
    /// </summary>
    /// <param name="category">The category data to create.</param>
    /// <returns>The created category.</returns>
    /// <response code="201">Returns the created category.</response>
    /// <response code="400">If the category data is invalid.</response>
    [HttpPost]
    [ProducesResponseType(typeof(Category), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Category category)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _categoryService.CreateAsync(category);
        return CreatedAtAction(null, new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates an existing category.
    /// </summary>
    /// <param name="id">The category identifier.</param>
    /// <param name="category">The updated category data.</param>
    /// <returns>The updated category.</returns>
    /// <response code="200">Returns the updated category.</response>
    /// <response code="404">If the category is not found.</response>
    /// <response code="400">If the category data is invalid.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(Category), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] Category category)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _categoryService.UpdateAsync(id, category);
        return Ok(updated);
    }

    /// <summary>
    /// Toggles the active/inactive status of a category.
    /// </summary>
    /// <param name="id">The category identifier.</param>
    /// <returns>The updated category.</returns>
    /// <response code="200">Returns the updated category.</response>
    /// <response code="404">If the category is not found.</response>
    [HttpPatch("{id}/toggle")]
    [ProducesResponseType(typeof(Category), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(int id)
    {
        var category = await _categoryService.ToggleActiveAsync(id);
        return Ok(category);
    }

    /// <summary>
    /// Deletes a category if it has no active products.
    /// </summary>
    /// <param name="id">The category identifier.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">Category deleted successfully.</response>
    /// <response code="400">If the category has active products.</response>
    /// <response code="404">If the category is not found.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _categoryService.DeleteAsync(id);
        return NoContent();
    }
}
