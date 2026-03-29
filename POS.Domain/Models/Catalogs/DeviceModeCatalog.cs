using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

public class DeviceModeCatalog
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? Description { get; set; }
}
