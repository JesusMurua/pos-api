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

    [MaxLength(1000)]
    public string? Description { get; set; }

    public bool IsAvailable { get; set; } = true;

    public bool IsPopular { get; set; }

    public bool TrackStock { get; set; } = false;

    public decimal CurrentStock { get; set; } = 0;

    public decimal LowStockThreshold { get; set; } = 0;

    public virtual Category? Category { get; set; }

    public virtual ICollection<ProductSize>? Sizes { get; set; }

    public virtual ICollection<ProductExtra>? Extras { get; set; }

    public virtual ICollection<ProductImage>? Images { get; set; }
}
