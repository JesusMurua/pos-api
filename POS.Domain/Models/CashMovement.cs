using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class CashMovement
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = null!;

    public int AmountCents { get; set; }

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual CashRegisterSession? Session { get; set; }
}
