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

        var business = new Business
        {
            Name = request.Name,
            PlanType = request.PlanType
        };

        var created = await _businessService.CreateAsync(business, UserId);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
