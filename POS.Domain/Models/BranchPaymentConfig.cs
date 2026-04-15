using System.ComponentModel.DataAnnotations;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

/// <summary>
/// Stores payment provider credentials per branch. One config per provider per branch.
/// </summary>
public class BranchPaymentConfig : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    /// <summary>Payment provider name: "mercadopago", "clip".</summary>
    [Required]
    [MaxLength(30)]
    public string Provider { get; set; } = null!;

    /// <summary>API access token / key for the provider, stored encrypted via IDataProtector.</summary>
    [Required]
    public string AccessToken { get; set; } = null!;

    /// <summary>Secret used to validate incoming webhooks from this provider.</summary>
    [MaxLength(255)]
    public string? WebhookSecret { get; set; }

    /// <summary>Physical terminal ID for providers that use terminal-based payments (e.g., Clip device ID).</summary>
    [MaxLength(100)]
    public string? TerminalId { get; set; }

    public bool IsActive { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Branch? Branch { get; set; }
}
