using System.ComponentModel.DataAnnotations.Schema;

namespace POS.Domain.Models;

public partial class Branch
{
    [NotMapped]
    public int TotalCategories => Categories?.Count ?? 0;

    [NotMapped]
    public int ActiveCategories => Categories?.Count(c => c.IsActive) ?? 0;
}
