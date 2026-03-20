using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class ProductSize
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Label { get; set; } = null!;

    public int ExtraPriceCents { get; set; }

    public virtual Product? Product { get; set; }
}
