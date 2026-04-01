using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>Request to create or update a delivery platform config for a branch.</summary>
public class UpsertDeliveryConfigRequest
{
    /// <summary>Platform to configure. Cannot be Direct.</summary>
    [Required]
    public OrderSource Platform { get; set; }

    public bool IsActive { get; set; } = false;

    [MaxLength(100)]
    public string? StoreId { get; set; }

    /// <summary>Plain text API key — will be encrypted before persisting.</summary>
    [MaxLength(500)]
    public string? ApiKey { get; set; }

    /// <summary>Plain text webhook secret — stored as-is (not encrypted, used for HMAC).</summary>
    [MaxLength(255)]
    public string? WebhookSecret { get; set; }
}
