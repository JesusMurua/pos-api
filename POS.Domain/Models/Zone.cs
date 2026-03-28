using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public class Zone
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    public ZoneType Type { get; set; } = ZoneType.Salon;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual Branch Branch { get; set; } = null!;
}
