using System.ComponentModel.DataAnnotations.Schema;

namespace POS.Domain.Models;

public partial class Order
{
    [NotMapped]
    public int TotalItems => Items?.Sum(i => i.Quantity) ?? 0;

    [NotMapped]
    public bool IsFullyPaid => TenderedCents.HasValue && TenderedCents.Value >= TotalCents;
}
