using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public class Zone : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    public ZoneType Type { get; set; } = ZoneType.Salon;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual Branch? Branch { get; set; }
}
