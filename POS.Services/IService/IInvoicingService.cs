namespace POS.Services.IService;

/// <summary>
/// Provides operations for issuing and managing CFDI electronic invoices via Facturapi.
/// </summary>
public interface IInvoicingService
{
    /// <summary>
    /// Creates a global invoice (factura global) consolidating all uninvoiced,
    /// paid, non-cancelled orders for a branch within a date range.
    /// Uses the generic RFC XAXX010101000 for public sales.
    /// </summary>
    /// <param name="startDate">Start of the consolidation period (UTC).</param>
    /// <param name="endDate">End of the consolidation period (UTC).</param>
    /// <param name="branchId">The branch identifier from JWT.</param>
    /// <returns>Summary of the global invoice created.</returns>
    Task<GlobalInvoiceResult> CreateGlobalInvoiceAsync(DateTime startDate, DateTime endDate, int branchId);

    /// <summary>
    /// Requests an individual invoice for a specific order,
    /// linked to a fiscal customer's RFC and tax data.
    /// </summary>
    /// <param name="orderId">The order UUID to invoice.</param>
    /// <param name="fiscalCustomerId">The fiscal customer who requested the invoice.</param>
    /// <param name="branchId">The branch identifier from JWT.</param>
    /// <returns>Summary of the individual invoice created.</returns>
    Task<IndividualInvoiceResult> RequestIndividualInvoiceAsync(string orderId, int fiscalCustomerId, int branchId);

    /// <summary>
    /// Gets public order details for the self-invoicing portal.
    /// Validates receipt proof via totalCents match. Returns only safe, non-sensitive data.
    /// </summary>
    /// <param name="orderId">The order UUID from the ticket QR/URL.</param>
    /// <param name="totalCents">The total amount from the physical receipt (receipt proof).</param>
    /// <returns>Safe DTO with branch name, date, total, and invoice status.</returns>
    Task<PublicOrderInvoiceInfo> GetPublicOrderDetailsAsync(string orderId, int totalCents);

    /// <summary>
    /// Requests an invoice from the public self-invoicing portal.
    /// Creates or reuses a FiscalCustomer, validates receipt proof, and issues the invoice.
    /// </summary>
    /// <param name="request">Order ID, receipt proof, and fiscal customer data.</param>
    /// <returns>Summary of the individual invoice created.</returns>
    Task<IndividualInvoiceResult> RequestPublicInvoiceAsync(PublicInvoiceRequest request);

    // ──────────────────────────────────────────
    // Invoice lifecycle: cancellation, webhooks, queries
    // ──────────────────────────────────────────

    /// <summary>
    /// Cancels an issued invoice via Facturapi and SAT.
    /// Unlinks all associated orders so they can be re-invoiced.
    /// </summary>
    /// <param name="invoiceId">The internal Invoice entity ID.</param>
    /// <param name="motive">SAT cancellation motive code (e.g., "02" = errors).</param>
    Task CancelInvoiceAsync(int invoiceId, string motive);

    /// <summary>
    /// Processes a Facturapi webhook event payload.
    /// Updates Invoice status, PDF/XML URLs, and linked Order statuses.
    /// </summary>
    /// <param name="eventType">The Facturapi event type (e.g., "invoice.status_updated").</param>
    /// <param name="rawJson">The full JSON payload from the webhook.</param>
    Task ProcessWebhookAsync(string eventType, string rawJson);

    /// <summary>
    /// Gets an invoice by its internal ID.
    /// </summary>
    Task<InvoiceDetailResult> GetInvoiceByIdAsync(int invoiceId);

    /// <summary>
    /// Gets all invoices linked to a specific order.
    /// </summary>
    Task<IEnumerable<InvoiceDetailResult>> GetInvoicesByOrderAsync(string orderId);

    /// <summary>
    /// Returns the download URL for an invoice document.
    /// </summary>
    /// <param name="invoiceId">The internal Invoice entity ID.</param>
    /// <param name="format">"pdf" or "xml".</param>
    Task<string> GetInvoiceDownloadUrlAsync(int invoiceId, string format);
}

/// <summary>
/// Result of a global invoice creation.
/// </summary>
public class GlobalInvoiceResult
{
    /// <summary>Number of orders included in the global invoice.</summary>
    public int OrderCount { get; set; }

    /// <summary>Total amount in cents of all consolidated orders.</summary>
    public int TotalCents { get; set; }

    /// <summary>Facturapi invoice ID (mock placeholder until live integration).</summary>
    public string? FacturapiId { get; set; }

    /// <summary>Current invoice status after creation.</summary>
    public string Status { get; set; } = null!;
}

/// <summary>
/// Result of an individual invoice creation.
/// </summary>
public class IndividualInvoiceResult
{
    /// <summary>The order UUID that was invoiced.</summary>
    public string OrderId { get; set; } = null!;

    /// <summary>RFC of the fiscal customer.</summary>
    public string CustomerRfc { get; set; } = null!;

    /// <summary>Total amount in cents.</summary>
    public int TotalCents { get; set; }

    /// <summary>Facturapi invoice ID (mock placeholder until live integration).</summary>
    public string? FacturapiId { get; set; }

    /// <summary>Current invoice status after creation.</summary>
    public string Status { get; set; } = null!;
}

/// <summary>
/// Safe DTO returned by the public self-invoicing portal. No sensitive data exposed.
/// </summary>
public class PublicOrderInvoiceInfo
{
    /// <summary>The order UUID.</summary>
    public string OrderId { get; set; } = null!;

    /// <summary>Business name (commercial name of the emitter).</summary>
    public string BusinessName { get; set; } = null!;

    /// <summary>Branch name where the order was placed.</summary>
    public string BranchName { get; set; } = null!;

    /// <summary>Date when the order was created.</summary>
    public DateTime Date { get; set; }

    /// <summary>Total amount in cents.</summary>
    public int TotalCents { get; set; }

    /// <summary>Current invoice status: None, Pending, Issued, Cancelled.</summary>
    public string InvoiceStatus { get; set; } = null!;

    /// <summary>Whether the business has electronic invoicing enabled.</summary>
    public bool InvoicingEnabled { get; set; }

    /// <summary>Whether the order can be invoiced right now.</summary>
    public bool CanInvoice { get; set; }

    /// <summary>URL to download the invoice PDF/XML. Only present when already invoiced.</summary>
    public string? InvoiceUrl { get; set; }
}

/// <summary>
/// Request payload for the public self-invoicing endpoint.
/// Includes receipt proof (TotalCents) and fiscal customer data.
/// </summary>
public class PublicInvoiceRequest
{
    /// <summary>The order UUID to invoice.</summary>
    public string OrderId { get; set; } = null!;

    /// <summary>Total amount in cents from the physical receipt (receipt proof).</summary>
    public int TotalCents { get; set; }

    /// <summary>RFC of the customer requesting the invoice.</summary>
    public string Rfc { get; set; } = null!;

    /// <summary>Legal name exactly as registered with SAT.</summary>
    public string FiscalName { get; set; } = null!;

    /// <summary>SAT tax regime code (e.g., "601", "612").</summary>
    public string TaxRegime { get; set; } = null!;

    /// <summary>Fiscal postal code (5 digits).</summary>
    public string ZipCode { get; set; } = null!;

    /// <summary>Email address to send the invoice to.</summary>
    public string? Email { get; set; }

    /// <summary>CFDI usage code (e.g., "G03"). Defaults to "G03" if not provided.</summary>
    public string? CfdiUse { get; set; }
}

/// <summary>
/// Detailed invoice result for query endpoints.
/// </summary>
public class InvoiceDetailResult
{
    public int Id { get; set; }
    public string Type { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? FacturapiId { get; set; }
    public string? Series { get; set; }
    public string? FolioNumber { get; set; }
    public int TotalCents { get; set; }
    public int SubtotalCents { get; set; }
    public int TaxCents { get; set; }
    public string? PaymentForm { get; set; }
    public string? PdfUrl { get; set; }
    public string? XmlUrl { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
