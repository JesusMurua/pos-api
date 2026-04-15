using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

/// <summary>
/// Represents a CFDI electronic invoice issued via Facturapi.
/// Supports both individual invoices (1 order) and global invoices (N orders).
/// </summary>
public class Invoice : IBranchScoped
{
    public int Id { get; set; }

    /// <summary>Business that issued this invoice (emisor).</summary>
    public int BusinessId { get; set; }

    /// <summary>Branch where the invoice was issued (lugar de expedición).</summary>
    public int BranchId { get; set; }

    /// <summary>Individual or Global invoice.</summary>
    public InvoiceType Type { get; set; }

    /// <summary>CFDI lifecycle status.</summary>
    public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;

    /// <summary>Facturapi invoice ID. Null until submitted to Facturapi.</summary>
    [MaxLength(50)]
    public string? FacturapiId { get; set; }

    /// <summary>FK to FiscalCustomer (receptor). Null for global invoices (público en general).</summary>
    public int? FiscalCustomerId { get; set; }

    /// <summary>CFDI series (e.g., "A").</summary>
    [MaxLength(10)]
    public string? Series { get; set; }

    /// <summary>CFDI folio number.</summary>
    [MaxLength(20)]
    public string? FolioNumber { get; set; }

    /// <summary>Total amount in cents (including taxes).</summary>
    public int TotalCents { get; set; }

    /// <summary>Subtotal before taxes in cents.</summary>
    public int SubtotalCents { get; set; }

    /// <summary>Total tax amount in cents.</summary>
    public int TaxCents { get; set; }

    /// <summary>Currency code. Always "MXN" for Mexican invoices.</summary>
    [MaxLength(3)]
    public string Currency { get; set; } = "MXN";

    /// <summary>SAT payment form code (e.g., "01" = Cash, "04" = Card).</summary>
    [MaxLength(2)]
    public string? PaymentForm { get; set; }

    /// <summary>SAT payment method. Always "PUE" for POS (Pago en Una sola Exhibición).</summary>
    [MaxLength(3)]
    public string PaymentMethod { get; set; } = "PUE";

    /// <summary>URL to download the invoice PDF.</summary>
    [MaxLength(500)]
    public string? PdfUrl { get; set; }

    /// <summary>URL to download the invoice XML (CFDI).</summary>
    [MaxLength(500)]
    public string? XmlUrl { get; set; }

    /// <summary>SAT cancellation reason code (e.g., "02" = Comprobante emitido con errores).</summary>
    [MaxLength(2)]
    public string? CancellationReason { get; set; }

    /// <summary>Timestamp when the CFDI was stamped by SAT (timbrado).</summary>
    public DateTime? IssuedAt { get; set; }

    /// <summary>Timestamp when the CFDI was cancelled.</summary>
    public DateTime? CancelledAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Business? Business { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual FiscalCustomer? FiscalCustomer { get; set; }

    /// <summary>Orders covered by this invoice. 1 for individual, N for global.</summary>
    public virtual ICollection<Order> Orders { get; set; } = new List<Order>();
}
