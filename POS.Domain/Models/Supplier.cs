using System.ComponentModel.DataAnnotations;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public class Supplier : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? ContactName { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch Branch { get; set; } = null!;

    public virtual ICollection<StockReceipt>? StockReceipts { get; set; }
}
