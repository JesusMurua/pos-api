using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class Product
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = null!;

    public int PriceCents { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public bool IsAvailable { get; set; } = true;

    public bool IsPopular { get; set; }

    public virtual Category? Category { get; set; }

    public virtual ICollection<ProductSize>? Sizes { get; set; }

    public virtual ICollection<ProductExtra>? Extras { get; set; }
}
