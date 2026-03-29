using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

public class KitchenStatusCatalog
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(10)]
    public string? Color { get; set; }

    public int SortOrder { get; set; }
}
