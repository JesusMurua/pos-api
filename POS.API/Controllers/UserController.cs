using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing users.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class UserController : BaseApiController
{
    private readonly IUserService _userService;

    public UserController(IUserService userService)
    {
        _userService = userService;
    }

    /// <summary>
    /// Gets all users for the current branch, including the business owner.
    /// </summary>
    /// <returns>A list of users.</returns>
    /// <response code="200">Returns the list of users.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<UserDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch()
    {
        var users = await _userService.GetByBranchAsync(BranchId);
        return Ok(users);
    }

    /// <summary>
    /// Creates a new user with PIN or email authentication.
    /// </summary>
    /// <param name="request">The user creation data.</param>
    /// <returns>The created user identifier.</returns>
    /// <response code="200">Returns the created user identifier.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _userService.CreateAsync(BranchId, request);
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
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _userService.UpdateAsync(id, request);
        return Ok(user);
    }

    /// <summary>
    /// Gets all branch assignments for a user.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <returns>A list of branch assignments.</returns>
    /// <response code="200">Returns the list of branch assignments.</response>
    /// <response code="404">If the user is not found.</response>
    [HttpGet("{id}/branches")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<UserBranchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBranches(int id)
    {
        var branches = await _userService.GetUserBranchesAsync(id);
        return Ok(branches);
    }

    /// <summary>
    /// Replaces all branch assignments for a user.
    /// </summary>
    /// <param name="id">The user identifier.</param>
    /// <param name="request">The branch assignment data.</param>
    /// <returns>The updated list of branch assignments.</returns>
    /// <response code="200">Returns the updated branch assignments.</response>
    /// <response code="400">If validation fails.</response>
    /// <response code="404">If the user is not found.</response>
    [HttpPost("{id}/branches")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(IEnumerable<UserBranchDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetBranches(int id, [FromBody] SetUserBranchesRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var branches = await _userService.SetUserBranchesAsync(id, request.BranchIds, request.DefaultBranchId);
        return Ok(branches);
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
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(int id)
    {
        var isActive = await _userService.ToggleActiveAsync(id);
        return Ok(new { isActive });
    }
}
