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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Order Order { get; set; } = null!;
}
