using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// Represents a fiscal customer (receptor) for CFDI electronic invoicing.
/// Stores RFC and tax data needed to issue individual invoices.
/// Scoped to Business — a single RFC is unique per emitter.
/// </summary>
public class FiscalCustomer
{
    public int Id { get; set; }

    /// <summary>Business that owns this fiscal customer record (the emitter).</summary>
    public int BusinessId { get; set; }

    /// <summary>RFC of the customer (tax ID). 12 chars for companies, 13 for individuals.</summary>
    [Required]
    [MaxLength(13)]
    public string Rfc { get; set; } = null!;

    /// <summary>Legal name of the customer exactly as registered with SAT.</summary>
    [Required]
    [MaxLength(300)]
    public string BusinessName { get; set; } = null!;

    /// <summary>SAT tax regime code of the customer (e.g., "601", "612").</summary>
    [Required]
    [MaxLength(3)]
    public string TaxRegime { get; set; } = null!;

    /// <summary>Fiscal postal code of the customer (domicilio fiscal). 5 digits.</summary>
    [Required]
    [MaxLength(5)]
    public string ZipCode { get; set; } = null!;

    /// <summary>Email address to send the invoice to.</summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>Default CFDI usage code (e.g., "G03" = Gastos generales).</summary>
    [MaxLength(5)]
    public string? CfdiUse { get; set; }

    /// <summary>Facturapi customer ID for this fiscal customer.</summary>
    [MaxLength(50)]
    public string? FacturapiCustomerId { get; set; }

    /// <summary>FK to Customer for CRM linking. Null if not linked to a CRM profile.</summary>
    public int? CustomerId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Business? Business { get; set; }

    public virtual Customer? Customer { get; set; }
}
