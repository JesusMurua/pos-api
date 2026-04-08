using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

public class OrderPayment
{
    public int Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = null!;

    public PaymentMethod Method { get; set; }

    public int AmountCents { get; set; }

    [MaxLength(50)]
    public string? Reference { get; set; }

    /// <summary>External provider name: "Clip", "MercadoPago", or null for manual payments.</summary>
    [MaxLength(30)]
    public string? PaymentProvider { get; set; }

    /// <summary>Transaction ID from the external provider (e.g., Clip transaction reference).</summary>
    [MaxLength(100)]
    public string? ExternalTransactionId { get; set; }

    /// <summary>JSON string with provider-specific data (terminal ID, receipt URL, etc.).</summary>
    public string? PaymentMetadata { get; set; }

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
