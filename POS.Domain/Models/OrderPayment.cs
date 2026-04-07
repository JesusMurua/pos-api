using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

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

    /// <summary>Payment lifecycle status: "completed", "pending", "failed", "refunded".</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = null!;

    /// <summary>Timestamp when the payment was confirmed by the external provider. Null for manual/pending payments.</summary>
    public DateTime? ConfirmedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Order Order { get; set; } = null!;
}
