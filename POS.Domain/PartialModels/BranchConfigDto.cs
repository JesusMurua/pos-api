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
    public bool HasKitchen { get; set; }
    public bool HasTables { get; set; }
    public bool HasDelivery { get; set; }
    public string? FolioPrefix { get; set; }
    public string? FolioFormat { get; set; }
    public int FolioCounter { get; set; }
    public int PlanTypeId { get; set; }
    public int PrimaryMacroCategoryId { get; set; }
    public string PosExperience { get; set; } = null!;

    /// <summary>
    /// IANA timezone identifier owned by the branch (added by BDD-015 to close the
    /// loop with BDD-013). Drives server-side per-day boundary math and timezone-aware
    /// UI rendering.
    /// </summary>
    public string TimeZoneId { get; set; } = "America/Mexico_City";
}
