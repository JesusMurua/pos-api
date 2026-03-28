using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing promotions and coupon validation.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class PromotionController : BaseApiController
{
    private readonly IPromotionService _promotionService;

    public PromotionController(IPromotionService promotionService)
    {
        _promotionService = promotionService;
    }

    /// <summary>
    /// Gets all promotions for the current branch.
    /// </summary>
    /// <returns>A list of promotions.</returns>
    /// <response code="200">Returns the list of promotions.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<Promotion>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch()
    {
        var promotions = await _promotionService.GetByBranchAsync(BranchId);
        return Ok(promotions);
    }

    /// <summary>
    /// Creates a new promotion for the current branch.
    /// </summary>
    /// <param name="promotion">The promotion data.</param>
    /// <returns>The created promotion.</returns>
    /// <response code="200">Returns the created promotion.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Promotion), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Promotion promotion)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        promotion.BranchId = BranchId;
        var created = await _promotionService.CreateAsync(promotion);
        return Ok(created);
    }

    /// <summary>
    /// Updates an existing promotion.
    /// </summary>
    /// <param name="id">The promotion identifier.</param>
    /// <param name="promotion">The updated promotion data.</param>
    /// <returns>The updated promotion.</returns>
    /// <response code="200">Returns the updated promotion.</response>
    /// <response code="404">If the promotion is not found.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Promotion), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] Promotion promotion)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _promotionService.UpdateAsync(id, promotion);
        return Ok(updated);
    }

    /// <summary>
    /// Deletes a promotion.
    /// </summary>
    /// <param name="id">The promotion identifier.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Promotion deleted.</response>
    /// <response code="404">If the promotion is not found.</response>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _promotionService.DeleteAsync(id);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Gets currently active promotions for a branch (public/kiosk).
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>A list of active promotions.</returns>
    /// <response code="200">Returns the list of active promotions.</response>
    [HttpGet("public/active")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<Promotion>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActivePublic([FromQuery] int branchId)
    {
        var promotions = await _promotionService.GetActiveByBranchAsync(branchId);
        return Ok(promotions);
    }

    /// <summary>
    /// Validates a coupon code for a branch (public).
    /// </summary>
    /// <param name="request">Branch ID and coupon code.</param>
    /// <returns>The valid promotion or 404.</returns>
    /// <response code="200">Returns the valid promotion.</response>
    /// <response code="404">If the coupon is invalid or exhausted.</response>
    [HttpPost("public/validate-coupon")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(Promotion), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ValidateCoupon([FromBody] ValidateCouponRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var promotion = await _promotionService.ValidateCouponAsync(request.BranchId, request.CouponCode);
        if (promotion == null)
            return NotFound(new { message = "Coupon code is invalid or exhausted" });

        return Ok(promotion);
    }
}

/// <summary>
/// Request body for coupon validation.
/// </summary>
public class ValidateCouponRequest
{
    [Required]
    public int BranchId { get; set; }

    [Required]
    public string CouponCode { get; set; } = null!;
}
