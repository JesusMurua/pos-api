using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing users.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Owner,Manager")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Gets all users for a branch, including the business owner.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>A list of users.</returns>
    /// <response code="200">Returns the list of users.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch([FromQuery] int branchId)
    {
        var users = await _userService.GetByBranchAsync(branchId);
        return Ok(users);
    }

    /// <summary>
    /// Creates a new user with PIN or email authentication.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="request">The user creation data.</param>
    /// <returns>The created user identifier.</returns>
    /// <response code="200">Returns the created user identifier.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromQuery] int branchId,
        [FromBody] CreateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _userService.CreateAsync(branchId, request);
        return Ok(new { id = user.Id });
    }

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="request">The update data.</param>
    /// <returns>The updated user.</returns>
    /// <response code="200">Returns the updated user.</response>
    /// <response code="404">If the user is not found.</response>
    [HttpPut("{id}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _userService.UpdateAsync(id, request);
        return Ok(user);
    }

    /// <summary>
    /// Toggles the active status of a user.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <returns>The new active status.</returns>
    /// <response code="200">Returns the new active status.</response>
    /// <response code="400">If trying to deactivate the last owner.</response>
    /// <response code="404">If the user is not found.</response>
    [HttpPatch("{id}/toggle")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(int id)
    {
        var isActive = await _userService.ToggleActiveAsync(id);
        return Ok(new { isActive });
    }
}
