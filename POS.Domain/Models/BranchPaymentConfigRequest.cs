using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class BranchPaymentConfigRequest
{
    /// <summary>Payment provider name: "mercadopago", "clip".</summary>
    [Required]
    [MaxLength(30)]
    public string Provider { get; set; } = null!;

    /// <summary>API access token / key for the provider. Will be stored encrypted.</summary>
    [Required]
    public string AccessToken { get; set; } = null!;

    /// <summary>Secret used to validate incoming webhooks from this provider.</summary>
    [MaxLength(255)]
    public string? WebhookSecret { get; set; }

    /// <summary>Physical terminal ID for providers that use terminal-based payments.</summary>
    [MaxLength(100)]
    public string? TerminalId { get; set; }

    public bool IsActive { get; set; } = false;
}
