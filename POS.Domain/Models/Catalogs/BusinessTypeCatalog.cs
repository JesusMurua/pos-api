using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

public class BusinessTypeCatalog
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    public bool HasKitchen { get; set; }
    public bool HasTables { get; set; }
    public int SortOrder { get; set; }
}
