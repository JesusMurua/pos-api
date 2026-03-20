using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class ProductExtra
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = null!;

    public int PriceCents { get; set; }

    public virtual Product? Product { get; set; }
}
