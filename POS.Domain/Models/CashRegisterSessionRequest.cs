using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class OpenSessionRequest
{
    public int InitialAmountCents { get; set; }

    [Required]
    [MaxLength(100)]
    public string OpenedBy { get; set; } = null!;
}

public class CloseSessionRequest
{
    public int CountedAmountCents { get; set; }

    [Required]
    [MaxLength(100)]
    public string ClosedBy { get; set; } = null!;

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class AddMovementRequest
{
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
}
