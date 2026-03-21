using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class CancelOrderRequest
{
    [Required]
    [MaxLength(500)]
    public string Reason { get; set; } = null!;

    [MaxLength(500)]
    public string? Notes { get; set; }
}
