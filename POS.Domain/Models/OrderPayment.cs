using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;
using POS.Domain.Models.Catalogs;
using PaymentMetadataModel = POS.Domain.Models.Metadata.PaymentMetadata;

namespace POS.Domain.Models;

public class OrderPayment
{
    public int Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = null!;

    public PaymentMethod Method { get; set; }

    /// <summary>
    /// Frozen-at-sale copy of the catalog row's <c>Code</c>. Denormalized so
    /// reports group without a join and survive catalog edits.
    /// </summary>
    [Required, MaxLength(20)]
    public string MethodCode { get; set; } = null!;

    /// <summary>Frozen-at-sale behavioral category — the report-bucket driver.</summary>
    public PaymentCategory Category { get; set; }

    /// <summary>Frozen-at-sale SAT "Forma de Pago" code for CFDI fidelity.</summary>
    [Required, MaxLength(2)]
    public string SatPaymentFormCode { get; set; } = null!;

    /// <summary>FK to <see cref="PaymentMethodCatalog"/> (RESTRICT). Queries read the frozen columns.</summary>
    public int PaymentMethodId { get; set; }

    /// <summary>
    /// Set when the method was valid but not authorized by the tenant's plan at
    /// sync time. Seted by PR-A2; present now to keep the schema atomic.
    /// </summary>
    public bool WasUnauthorized { get; set; }

    /// <summary>
    /// Set when the method was absent from the catalog and recorded as a fallback.
    /// Seted by PR-A2; present now to keep the schema atomic.
    /// </summary>
    public bool WasUnknownMethod { get; set; }

    public int AmountCents { get; set; }

    [MaxLength(50)]
    public string? Reference { get; set; }

    /// <summary>External provider name: "Clip", "MercadoPago", or null for manual payments.</summary>
    [MaxLength(30)]
    public string? PaymentProvider { get; set; }

    /// <summary>Transaction ID from the external provider (e.g., Clip transaction reference).</summary>
    [MaxLength(100)]
    public string? ExternalTransactionId { get; set; }

    /// <summary>
    /// Provider-specific payment metadata persisted as PostgreSQL <c>jsonb</c>
    /// via EF Core 9 owned-type JSON mapping. Captures terminal/processor data
    /// (Clip, MercadoPago, etc.) as strict typed properties.
    /// </summary>
    public PaymentMetadataModel? PaymentMetadata { get; set; }

    /// <summary>
    /// Dynamic tenant-specific data. CRITICAL: Lifecycle is managed by EF.
    /// Access RootElement for reads, but CLONE/COPY values if the entity will
    /// be detached/disposed to avoid ObjectDisposedException.
    /// </summary>
    public System.Text.Json.JsonDocument? ExtensionData { get; set; }

    /// <summary>Internal tracking ID for the terminal operation, assigned by the POS.</summary>
    [MaxLength(100)]
    public string? OperationId { get; set; }

    /// <summary>FK to PaymentStatusCatalog.Id (1=Pending, 2=Completed, 3=Failed, 4=Refunded).</summary>
    public int PaymentStatusId { get; set; } = Helpers.PaymentStatus.Pending;

    /// <summary>Timestamp when the payment was confirmed by the external provider. Null for manual/pending payments.</summary>
    public DateTime? ConfirmedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Order Order { get; set; } = null!;

    public PaymentStatusCatalog? PaymentStatus { get; set; }
}
