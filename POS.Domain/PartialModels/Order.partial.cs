using System.ComponentModel.DataAnnotations.Schema;

namespace POS.Domain.Models;

public partial class Order
{
    [NotMapped]
    public int TotalItems => Items?.Count ?? 0;

    [NotMapped]
    public bool IsFullyPaid => PaidCents >= TotalCents;

    [NotMapped]
    public int RemainingCents => Math.Max(0, TotalCents - PaidCents);
}
