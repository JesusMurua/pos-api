using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.DTOs.Customer;
using POS.Services.IService;
using ValidationException = POS.Domain.Exceptions.ValidationException;

namespace POS.API.Controllers;

/// <summary>
/// Cross-customer membership reads. Per-customer endpoints live on
/// <see cref="CustomersController"/>; this controller serves dashboard widgets
/// that aggregate or scan across the tenant's full membership population.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class MembershipsController : BaseApiController
{
    private readonly IMembershipService _membershipService;

    public MembershipsController(IMembershipService membershipService)
    {
        _membershipService = membershipService;
    }

    #region Admin Dashboard widgets

    /// <summary>
    /// Returns memberships expiring within the next <paramref name="days"/>
    /// days for the caller's tenant. Sorted by <c>ValidUntil</c> ascending so
    /// the closest expirations surface first. Powers the Admin Dashboard
    /// "Expiring Soon" widget without forcing client-side iteration over the
    /// full membership population.
    /// </summary>
    /// <param name="days">
    /// Lookahead window in days. Defaults to 7. Values greater than 30 are
    /// silently capped at 30 to bound query cost; values less than 1 produce
    /// a <c>400 INVALID_DAYS</c>.
    /// </param>
    /// <response code="200">Returns the expiring memberships ordered by <c>ValidUntil</c> ascending.</response>
    /// <response code="400"><c>INVALID_DAYS</c> — <paramref name="days"/> is less than 1.</response>
    [HttpGet("expiring")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<CustomerMembershipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetExpiring([FromQuery] int days = 7)
    {
        try
        {
            // Validate first (fail-fast), then cap silently.
            if (days < 1)
                throw new ValidationException("INVALID_DAYS: days must be >= 1.");
            if (days > 30) days = 30;

            var result = await _membershipService.GetExpiringSoonAsync(BusinessId, days);
            return Ok(result);
        }
        catch (ValidationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    #endregion
}
