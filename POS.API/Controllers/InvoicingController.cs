using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Filters;
using POS.Domain.Enums;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for electronic invoicing (CFDI 4.0) operations.
/// </summary>
[Route("api/[controller]")]
[Authorize]
[RequiresFeature(FeatureKey.CfdiInvoicing)]
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

    /// <summary>
    /// Gets an invoice by its internal ID.
    /// </summary>
    /// <param name="id">The invoice identifier.</param>
    /// <returns>Invoice details.</returns>
    /// <response code="200">Returns the invoice details.</response>
    /// <response code="404">If the invoice is not found.</response>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(InvoiceDetailResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _invoicingService.GetInvoiceByIdAsync(id);
        return Ok(result);
    }

    /// <summary>
    /// Gets all invoices linked to a specific order.
    /// </summary>
    /// <param name="orderId">The order UUID.</param>
    /// <returns>List of invoices for the order.</returns>
    /// <response code="200">Returns the invoices.</response>
    [HttpGet("by-order/{orderId}")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<InvoiceDetailResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByOrder(string orderId)
    {
        var result = await _invoicingService.GetInvoicesByOrderAsync(orderId);
        return Ok(result);
    }

    /// <summary>
    /// Downloads an invoice document (PDF or XML).
    /// </summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="format">The format: "pdf" or "xml".</param>
    /// <returns>Redirect to the download URL.</returns>
    /// <response code="302">Redirects to the document URL.</response>
    /// <response code="404">If the invoice is not found.</response>
    /// <response code="400">If the format is invalid or document not available.</response>
    [HttpGet("{id:int}/download/{format}")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Download(int id, string format)
    {
        var url = await _invoicingService.GetInvoiceDownloadUrlAsync(id, format);
        return Redirect(url);
    }

    /// <summary>
    /// Cancels an issued invoice at SAT via Facturapi.
    /// Unlinks all associated orders so they can be re-invoiced.
    /// </summary>
    /// <param name="id">The invoice identifier.</param>
    /// <param name="request">The cancellation reason.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Invoice cancelled successfully.</response>
    /// <response code="400">If the invoice cannot be cancelled.</response>
    /// <response code="404">If the invoice is not found.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelInvoiceRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _invoicingService.CancelInvoiceAsync(id, request.Motive);
        return Ok(new { success = true });
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

/// <summary>
/// Request body for cancelling an invoice.
/// </summary>
public class CancelInvoiceRequest
{
    /// <summary>SAT cancellation motive code: "01" = receipt with errors, "02" = receipt with unrelated data, "03" = operation not performed, "04" = related to global invoice.</summary>
    [Required]
    [MaxLength(2)]
    public string Motive { get; set; } = null!;
}
