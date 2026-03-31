using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Models;
using POS.Domain.Models;
using POS.Domain.PartialModels;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing branch configuration and PIN.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class BranchController : BaseApiController
{
    private readonly IBranchService _branchService;

    public BranchController(IBranchService branchService)
    {
        _branchService = branchService;
    }

    /// <summary>
    /// Gets all branches for the current business.
    /// </summary>
    /// <returns>A list of branches.</returns>
    /// <response code="200">Returns the list of branches.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<Branch>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBusiness()
    {
        var branches = await _branchService.GetByBusinessAsync(BusinessId);
        return Ok(branches);
    }

    /// <summary>
    /// Creates a new branch for the current business.
    /// </summary>
    /// <param name="request">The branch data to create.</param>
    /// <returns>The created branch identifier.</returns>
    /// <response code="200">Returns the created branch identifier.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBranchRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var branch = new Branch
        {
            BusinessId = BusinessId,
            Name = request.Name,
            LocationName = request.LocationName
        };

        var created = await _branchService.CreateAsync(branch);
        return Ok(new { id = created.Id });
    }

    /// <summary>
    /// Updates an existing branch.
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <param name="request">The updated branch data.</param>
    /// <returns>The updated branch.</returns>
    /// <response code="200">Returns the updated branch.</response>
    /// <response code="404">If the branch is not found.</response>
    /// <response code="400">If the request data is invalid or branch doesn't belong to the business.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Branch), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateConfigRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var branch = new Branch
        {
            BusinessId = BusinessId,
            Name = request.Name,
            LocationName = request.LocationName,
            HasKitchen = request.HasKitchen ?? false,
            HasTables = request.HasTables ?? false
        };

        var updated = await _branchService.UpdateAsync(id, branch, request.HasKitchen, request.HasTables);
        return Ok(updated);
    }

    /// <summary>
    /// Copies the catalog (categories, products, sizes, extras) from a source branch.
    /// Skips if the target branch already has categories.
    /// </summary>
    /// <param name="id">The target branch identifier.</param>
    /// <param name="request">The source branch to copy from.</param>
    /// <returns>The number of products copied.</returns>
    /// <response code="200">Returns the number of products copied.</response>
    /// <response code="400">If the target already has categories or branches don't match.</response>
    /// <response code="404">If a branch is not found.</response>
    [HttpPost("{id}/copy-catalog")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CopyCatalog(int id, [FromBody] CopyCatalogRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var count = await _branchService.CopyCatalogAsync(id, request.SourceBranchId);
        return Ok(new { productsCopied = count });
    }

    /// <summary>
    /// Retrieves branch configuration with business data as a flat DTO.
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <returns>The branch configuration.</returns>
    /// <response code="200">Returns the branch configuration.</response>
    /// <response code="404">If the branch is not found.</response>
    [HttpGet("{id}/config")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(BranchConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfig(int id)
    {
        var config = await _branchService.GetBranchConfigDtoAsync(id);
        return Ok(config);
    }

    /// <summary>
    /// Retrieves public branch info for kiosk mode (no sensitive data).
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <returns>Basic branch info.</returns>
    /// <response code="200">Returns the branch public info.</response>
    /// <response code="404">If the branch is not found.</response>
    [HttpGet("public/{id}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicConfig(int id)
    {
        var branch = await _branchService.GetConfigAsync(id);
        return Ok(new
        {
            branch.Id,
            branch.Name,
            branch.LocationName,
            businessName = branch.Business?.Name
        });
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
    [Authorize(Roles = "Owner")]
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
    [Authorize(Roles = "Owner")]
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
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePin(int id, [FromBody] UpdatePinRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _branchService.UpdatePinAsync(id, request.CurrentPin, request.NewPin);
        return Ok(new { message = "PIN updated successfully" });
    }

    /// <summary>
    /// Updates the folio configuration for a branch.
    /// </summary>
    /// <param name="request">Folio prefix and format.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Folio config updated.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPut("folio-config")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateFolioConfig([FromBody] FolioConfigRequest request)
    {
        if (request.FolioPrefix?.Length > 10)
            return BadRequest(new { message = "Prefix must be 10 characters or less" });

        var branch = await _branchService.GetConfigAsync(BranchId);
        branch.FolioPrefix = request.FolioPrefix;
        branch.FolioFormat = request.FolioFormat;
        await _branchService.UpdateAsync(BranchId, branch);

        return Ok(new { message = "Folio config updated" });
    }

    /// <summary>
    /// Updates branch feature settings (kitchen, tables).
    /// </summary>
    /// <param name="id">The branch identifier.</param>
    /// <param name="request">The settings to update.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Settings updated.</response>
    /// <response code="404">If the branch is not found.</response>
    [HttpPatch("{id}/settings")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateSettings(int id, [FromBody] BranchSettingsRequest request)
    {
        var branch = await _branchService.GetConfigAsync(id);
        branch.HasKitchen = request.HasKitchen;
        branch.HasTables = request.HasTables;
        await _branchService.UpdateAsync(id, branch);

        return Ok(new { message = "Branch settings updated" });
    }
}

/// <summary>
/// Request body for folio configuration.
/// </summary>
public class FolioConfigRequest
{
    public string? FolioPrefix { get; set; }
    public string? FolioFormat { get; set; }
}

/// <summary>
/// Request body for branch feature settings.
/// </summary>
public class BranchSettingsRequest
{
    public bool HasKitchen { get; set; }
    public bool HasTables { get; set; }
}
