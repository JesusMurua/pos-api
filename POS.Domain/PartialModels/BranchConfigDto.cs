namespace POS.Domain.PartialModels;

/// <summary>
/// Flat DTO for branch configuration with business data denormalized.
/// </summary>
public class BranchConfigDto
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string BusinessName { get; set; } = null!;
    public string BranchName { get; set; } = null!;
    public string? LocationName { get; set; }
    public bool HasKitchen { get; set; }
    public bool HasTables { get; set; }
    public bool HasDelivery { get; set; }
    public string? FolioPrefix { get; set; }
    public string? FolioFormat { get; set; }
    public int FolioCounter { get; set; }
    public string PlanType { get; set; } = null!;
    public string BusinessType { get; set; } = null!;
    public string PosExperience { get; set; } = null!;
}
