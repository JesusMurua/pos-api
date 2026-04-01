using POS.Domain.Enums;

namespace POS.Domain.Models;

public class BranchDeliveryConfig
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    /// <summary>Delivery platform. Never OrderSource.Direct.</summary>
    public OrderSource Platform { get; set; }

    /// <summary>Whether this platform integration is active for this branch.</summary>
    public bool IsActive { get; set; } = false;

    /// <summary>Store/restaurant ID assigned by the delivery platform.</summary>
    public string? StoreId { get; set; }

    /// <summary>API key from the delivery platform, stored encrypted via IDataProtector.</summary>
    public string? ApiKeyEncrypted { get; set; }

    /// <summary>Secret used to validate incoming webhooks from this platform.</summary>
    public string? WebhookSecret { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Branch? Branch { get; set; }
}
