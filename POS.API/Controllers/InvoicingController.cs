using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for electronic invoicing (CFDI 4.0) operations.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class InvoicingController : BaseApiController
{
    private readonly IInvoicingService _invoicingService;

    public InvoicingController(IInvoicingService invoicingService)
    {
        _invoicingService = invoicingService;
    }

    /// <summary>
    /// Creates a global invoice (factura global) consolidating all uninvoiced,
    /// paid orders for the current branch within a date range.
    /// </summary>
    /// <param name="request">The date range for the global invoice.</param>
    /// <returns>Summary of the global invoice created.</returns>
    /// <response code="200">Returns the global invoice summary.</response>
    /// <response code="400">If no orders found or validation fails.</response>
    [HttpPost("global")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(GlobalInvoiceResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateGlobalInvoice([FromBody] CreateGlobalInvoiceRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _invoicingService.CreateGlobalInvoiceAsync(
            request.StartDate, request.EndDate, BranchId);

        return Ok(result);
    }

    /// <summary>
    /// Creates an individual invoice for a specific order linked to a fiscal customer.
    /// </summary>
    /// <param name="request">The order ID and fiscal customer ID.</param>
    /// <returns>Summary of the individual invoice created.</returns>
    /// <response code="200">Returns the individual invoice summary.</response>
    /// <response code="400">If validation fails (cancelled, already invoiced, etc.).</response>
    /// <response code="404">If the order or fiscal customer is not found.</response>
    [HttpPost("individual")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IndividualInvoiceResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestIndividualInvoice([FromBody] RequestIndividualInvoiceRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _invoicingService.RequestIndividualInvoiceAsync(
            request.OrderId, request.FiscalCustomerId, BranchId);

        return Ok(result);
    }
}

/// <summary>
/// Request body for creating a global invoice.
/// </summary>
public class CreateGlobalInvoiceRequest
{
    /// <summary>Start of the consolidation period (UTC).</summary>
    [Required]
    public DateTime StartDate { get; set; }

    /// <summary>End of the consolidation period (UTC).</summary>
    [Required]
    public DateTime EndDate { get; set; }
}

/// <summary>
/// Request body for requesting an individual invoice.
/// </summary>
public class RequestIndividualInvoiceRequest
{
    /// <summary>The order UUID to invoice.</summary>
    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = null!;

    /// <summary>The fiscal customer ID who requested the invoice.</summary>
    [Required]
    public int FiscalCustomerId { get; set; }
}
