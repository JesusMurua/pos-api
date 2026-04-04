using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Public (unauthenticated) endpoints for customer self-invoicing.
/// Accessed via the ticket URL (e.g., kaja.mx/factura?orderId=abc-123).
/// Secured by receipt proof (TotalCents match) and rate limiting.
/// </summary>
[Route("api/public/invoicing")]
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("PublicInvoicingPolicy")]
public class PublicInvoicingController : ControllerBase
{
    private readonly IInvoicingService _invoicingService;

    public PublicInvoicingController(IInvoicingService invoicingService)
    {
        _invoicingService = invoicingService;
    }

    /// <summary>
    /// Gets public order details for the self-invoicing portal.
    /// Returns only safe data: business name, date, total, invoice status.
    /// Requires totalCents as receipt proof (must match the physical ticket).
    /// </summary>
    /// <param name="orderId">The order UUID from the ticket QR/URL.</param>
    /// <param name="totalCents">The total amount from the physical receipt.</param>
    /// <returns>Safe order details for the invoicing form.</returns>
    /// <response code="200">Returns the order details.</response>
    /// <response code="401">If the totalCents does not match (receipt proof failed).</response>
    /// <response code="404">If the order is not found.</response>
    /// <response code="429">If rate limit exceeded.</response>
    [HttpGet("{orderId}")]
    [ProducesResponseType(typeof(PublicOrderInvoiceInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GetOrderDetails(string orderId, [FromQuery] int totalCents)
    {
        var info = await _invoicingService.GetPublicOrderDetailsAsync(orderId, totalCents);
        return Ok(info);
    }

    /// <summary>
    /// Requests an individual invoice from the public self-invoicing portal.
    /// Creates or reuses a FiscalCustomer with the provided RFC and tax data.
    /// Requires totalCents as receipt proof (must match the physical ticket).
    /// </summary>
    /// <param name="request">Order ID, receipt proof, and fiscal customer data.</param>
    /// <returns>Summary of the invoice created.</returns>
    /// <response code="200">Returns the invoice summary.</response>
    /// <response code="400">If validation fails (cancelled, already invoiced, invoicing disabled, etc.).</response>
    /// <response code="401">If the totalCents does not match (receipt proof failed).</response>
    /// <response code="404">If the order is not found.</response>
    /// <response code="429">If rate limit exceeded.</response>
    [HttpPost("request")]
    [ProducesResponseType(typeof(IndividualInvoiceResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestInvoice([FromBody] PublicInvoiceApiRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var serviceRequest = new PublicInvoiceRequest
        {
            OrderId = request.OrderId,
            TotalCents = request.TotalCents,
            Rfc = request.Rfc,
            FiscalName = request.FiscalName,
            TaxRegime = request.TaxRegime,
            ZipCode = request.ZipCode,
            Email = request.Email,
            CfdiUse = request.CfdiUse
        };

        var result = await _invoicingService.RequestPublicInvoiceAsync(serviceRequest);
        return Ok(result);
    }
}

/// <summary>
/// Request body for the public self-invoicing endpoint.
/// </summary>
public class PublicInvoiceApiRequest
{
    /// <summary>The order UUID from the ticket QR/URL.</summary>
    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = null!;

    /// <summary>Total amount in cents from the physical receipt (receipt proof).</summary>
    [Required]
    public int TotalCents { get; set; }

    /// <summary>RFC of the customer requesting the invoice (12-13 chars).</summary>
    [Required]
    [MaxLength(13)]
    public string Rfc { get; set; } = null!;

    /// <summary>Legal name exactly as registered with SAT.</summary>
    [Required]
    [MaxLength(300)]
    public string FiscalName { get; set; } = null!;

    /// <summary>SAT tax regime code (e.g., "601", "612").</summary>
    [Required]
    [MaxLength(3)]
    public string TaxRegime { get; set; } = null!;

    /// <summary>Fiscal postal code (5 digits).</summary>
    [Required]
    [MaxLength(5)]
    public string ZipCode { get; set; } = null!;

    /// <summary>Email address to send the invoice to.</summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>CFDI usage code (e.g., "G03"). Defaults to "G03" if not provided.</summary>
    [MaxLength(5)]
    public string? CfdiUse { get; set; }
}
