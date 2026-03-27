using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class ProductImage
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    [Required]
    [MaxLength(2048)]
    public string Url { get; set; } = null!;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Product Product { get; set; } = null!;
}
