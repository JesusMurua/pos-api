using System.ComponentModel.DataAnnotations.Schema;

namespace POS.Domain.Models;

public partial class Business
{
    [NotMapped]
    public int TotalBranches => Branches?.Count ?? 0;

    [NotMapped]
    public int ActiveBranches => Branches?.Count(b => b.IsActive) ?? 0;
}
