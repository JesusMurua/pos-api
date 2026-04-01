using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing reservations.
/// </summary>
[Route("api/reservations")]
public class ReservationsController : BaseApiController
{
    private readonly IReservationService _reservationService;

    public ReservationsController(IReservationService reservationService)
    {
        _reservationService = reservationService;
    }

    /// <summary>
    /// Retrieves reservations for a specific date.
    /// </summary>
    [HttpGet("day")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(typeof(IEnumerable<Reservation>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByDate([FromQuery] DateOnly date)
    {
        var reservations = await _reservationService.GetByDateAsync(BranchId, date);
        return Ok(reservations);
    }

    /// <summary>
    /// Retrieves reservations for a specific month.
    /// </summary>
    [HttpGet("month")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(typeof(IEnumerable<Reservation>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByMonth([FromQuery] int year, [FromQuery] int month)
    {
        var reservations = await _reservationService.GetByMonthAsync(BranchId, year, month);
        return Ok(reservations);
    }

    /// <summary>
    /// Creates a new reservation.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(typeof(Reservation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Reservation reservation)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var created = await _reservationService.CreateAsync(reservation, BranchId, UserId);
            return Ok(created);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Updates an existing reservation.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(typeof(Reservation), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] Reservation reservation)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var updated = await _reservationService.UpdateAsync(id, reservation, BranchId);
            return Ok(updated);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Confirms a pending reservation.
    /// </summary>
    [HttpPatch("{id}/confirm")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm(int id)
    {
        try
        {
            await _reservationService.ConfirmAsync(id, BranchId);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Cancels a reservation.
    /// </summary>
    [HttpPatch("{id}/cancel")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(int id)
    {
        try
        {
            await _reservationService.CancelAsync(id, BranchId);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Marks a reservation as no-show.
    /// </summary>
    [HttpPatch("{id}/no-show")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> NoShow(int id)
    {
        try
        {
            await _reservationService.MarkNoShowAsync(id, BranchId);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Marks a reservation as seated and updates table status.
    /// </summary>
    [HttpPatch("{id}/seat")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Seat(int id)
    {
        try
        {
            await _reservationService.SeatAsync(id, BranchId);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Checks if a table is available for a given date, time, and duration.
    /// </summary>
    [HttpGet("availability")]
    [Authorize(Roles = "Owner,Manager,Host")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckAvailability(
        [FromQuery] int tableId,
        [FromQuery] DateOnly date,
        [FromQuery] TimeOnly time,
        [FromQuery] int duration = 90,
        [FromQuery] int? excludeId = null)
    {
        var available = await _reservationService.CheckAvailabilityAsync(tableId, date, time, duration, excludeId);
        return Ok(new { available });
    }
}
