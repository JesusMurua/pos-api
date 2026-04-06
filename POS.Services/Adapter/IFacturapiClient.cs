namespace POS.Services.Adapter;

/// <summary>
/// Adapter interface for Facturapi REST API calls.
/// Abstracts HTTP communication for testability.
/// </summary>
public interface IFacturapiClient
{
    /// <summary>
    /// Creates a customer (receptor) in Facturapi. Returns the Facturapi customer ID.
    /// </summary>
    Task<FacturapiCustomerResponse> CreateCustomerAsync(FacturapiCustomerRequest request);

    /// <summary>
    /// Creates an individual CFDI invoice via Facturapi.
    /// </summary>
    Task<FacturapiInvoiceResponse> CreateInvoiceAsync(FacturapiInvoiceRequest request);

    /// <summary>
    /// Creates a global CFDI invoice (factura global) via Facturapi.
    /// </summary>
    Task<FacturapiInvoiceResponse> CreateGlobalInvoiceAsync(FacturapiGlobalInvoiceRequest request);

    /// <summary>
    /// Cancels an existing invoice in Facturapi.
    /// </summary>
    Task<FacturapiInvoiceResponse> CancelInvoiceAsync(string facturapiInvoiceId, string cancellationReason);
}

#region Request / Response DTOs

public class FacturapiCustomerRequest
{
    public string LegalName { get; set; } = null!;
    public string Rfc { get; set; } = null!;
    public string TaxSystem { get; set; } = null!;
    public string Zip { get; set; } = null!;
    public string? Email { get; set; }
}

public class FacturapiCustomerResponse
{
    public string Id { get; set; } = null!;
}

public class FacturapiInvoiceItem
{
    public string ProductCode { get; set; } = null!;
    public string UnitCode { get; set; } = null!;
    public string Description { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal TaxRate { get; set; }
}

public class FacturapiInvoiceRequest
{
    public string CustomerId { get; set; } = null!;
    public string PaymentForm { get; set; } = null!;
    public string PaymentMethod { get; set; } = "PUE";
    public string Use { get; set; } = "G03";
    public List<FacturapiInvoiceItem> Items { get; set; } = new();
}

public class FacturapiGlobalInvoiceRequest
{
    public string Periodicity { get; set; } = "month";
    public int Month { get; set; }
    public int Year { get; set; }
    public List<FacturapiInvoiceItem> Items { get; set; } = new();
}

public class FacturapiInvoiceResponse
{
    public string Id { get; set; } = null!;
    public string? Status { get; set; }
    public string? Series { get; set; }
    public string? FolioNumber { get; set; }
    public decimal? Total { get; set; }
    public string? PdfUrl { get; set; }
    public string? XmlUrl { get; set; }
}

#endregion
