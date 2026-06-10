using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only surface over SaaS <c>SubscriptionInvoice</c>s and their payments
/// (tenant → operator billing, manual rails). One controller covers both the
/// per-business and per-invoice route shapes (§9). Authenticated via <c>X-Admin-Token</c>.
/// Stripe-rail invoices are produced by the worker, not here. See
/// docs/saas-billing-architecture.md §6/§9.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminInvoicesController : ControllerBase
{
    private readonly IAdminInvoiceService _invoices;
    private readonly IAdminTenantPaymentService _payments;

    public AdminInvoicesController(IAdminInvoiceService invoices, IAdminTenantPaymentService payments)
    {
        _invoices = invoices;
        _payments = payments;
    }

    private string? AdminTokenId =>
        User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value;

    /// <summary>All SaaS invoices for a business (newest first).</summary>
    [HttpGet("api/Admin/businesses/{businessId:int}/invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminInvoiceListItemDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForBusiness(int businessId) =>
        Ok(await _invoices.GetForBusinessAsync(businessId));

    /// <summary>Create a manual invoice for a business.</summary>
    [HttpPost("api/Admin/businesses/{businessId:int}/invoices")]
    [ProducesResponseType(typeof(AdminInvoiceDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(int businessId, [FromBody] AdminCreateInvoiceRequest request)
    {
        var detail = await _invoices.CreateAsync(businessId, request, AdminTokenId);
        return CreatedAtAction(nameof(Get), new { id = detail.Id }, detail);
    }

    /// <summary>Invoice detail (items + payments).</summary>
    [HttpGet("api/Admin/invoices/{id:int}")]
    [ProducesResponseType(typeof(AdminInvoiceDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id) => Ok(await _invoices.GetAsync(id));

    /// <summary>Edit an invoice while it is still Open.</summary>
    [HttpPut("api/Admin/invoices/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] AdminUpdateInvoiceRequest request)
    {
        await _invoices.UpdateAsync(id, request, AdminTokenId);
        return NoContent();
    }

    /// <summary>Void an invoice (only from {Open, Overdue}).</summary>
    [HttpPost("api/Admin/invoices/{id:int}/void")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Void(int id, [FromBody] AdminVoidInvoiceRequest request)
    {
        await _invoices.VoidAsync(id, request.Reason, AdminTokenId);
        return NoContent();
    }

    /// <summary>Record a payment against an invoice (manual rails).</summary>
    [HttpPost("api/Admin/invoices/{id:int}/payments")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecordPayment(int id, [FromBody] AdminRecordPaymentRequest request)
    {
        await _payments.RecordAsync(
            invoiceId: id,
            billingMethodId: request.BillingMethodId,
            amountCents: request.AmountCents,
            currency: request.Currency,
            paidAtUtc: request.PaidAtUtc ?? DateTime.UtcNow,
            reference: request.Reference,
            notes: request.Notes,
            receivedByTokenIdHash: AdminTokenId,
            stripeChargeId: null,
            rawWebhookPayloadJson: null);
        return NoContent();
    }

    /// <summary>Delete a recorded payment (capture-error fix).</summary>
    [HttpDelete("api/Admin/invoices/{id:int}/payments/{paymentId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePayment(int id, int paymentId)
    {
        await _payments.DeleteAsync(id, paymentId, AdminTokenId);
        return NoContent();
    }
}
