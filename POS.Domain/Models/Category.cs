using System.ComponentModel.DataAnnotations;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public partial class Category : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(50)]
    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<Product>? Products { get; set; }
}
