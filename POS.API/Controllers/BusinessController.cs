using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Models;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing businesses.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class BusinessController : BaseApiController
{
    private readonly IBusinessService _businessService;

    public BusinessController(IBusinessService businessService)
    {
        _businessService = businessService;
    }

    /// <summary>
    /// Retrieves a business by its identifier with branches.
    /// </summary>
    /// <param name="id">The business identifier.</param>
    /// <returns>The requested business.</returns>
    /// <response code="200">Returns the requested business.</response>
    /// <response code="404">If the business is not found.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Business), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var business = await _businessService.GetByIdAsync(id);
        return Ok(business);
    }

    /// <summary>
    /// Creates a new business.
    /// </summary>
    /// <param name="request">The business data to create.</param>
    /// <returns>The created business.</returns>
    /// <response code="201">Returns the created business.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Business), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBusinessRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (!Enum.TryParse<POS.Domain.Enums.PlanType>(request.PlanType, true, out var planType))
            planType = POS.Domain.Enums.PlanType.Free;

        var business = new Business
        {
            Name = request.Name,
            PlanType = planType
        };

        var created = await _businessService.CreateAsync(business, UserId);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates the business type (giro).
    /// </summary>
    /// <param name="request">The new business type.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Business type updated.</response>
    /// <response code="400">If the type is invalid.</response>
    [HttpPut("type")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateType([FromBody] UpdateBusinessTypeRequest request)
    {
        if (!Enum.TryParse<POS.Domain.Enums.BusinessType>(request.BusinessType, true, out var businessType))
            return BadRequest(new { message = "Invalid business type" });

        var business = await _businessService.GetByIdAsync(BusinessId);
        business.BusinessType = businessType;
        await _businessService.UpdateAsync(business);
        return Ok(new { message = "Business type updated", businessType = businessType.ToString() });
    }
}

/// <summary>
/// Request body for updating business type.
/// </summary>
public class UpdateBusinessTypeRequest
{
    public string BusinessType { get; set; } = null!;
}
