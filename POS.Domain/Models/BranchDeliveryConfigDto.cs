using POS.Domain.Enums;

namespace POS.Domain.Models;

public class BranchDeliveryConfigDto
{
    public int Id { get; set; }

    public OrderSource Platform { get; set; }

    public bool IsActive { get; set; }

    public string? StoreId { get; set; }

    /// <summary>True if an API key has been stored. Never expose the key itself.</summary>
    public bool HasApiKey { get; set; }

    /// <summary>True if a webhook secret has been configured.</summary>
    public bool HasWebhookSecret { get; set; }

    /// <summary>Webhook URL the platform should call for this branch.</summary>
    public string WebhookUrl { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}
