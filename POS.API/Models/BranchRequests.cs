using System.ComponentModel.DataAnnotations;

namespace POS.API.Models;

/// <summary>
/// Admin-wide branch update (Name, location, kitchen/tables flags).
/// Consumed by <c>PUT /api/branch/{id}</c>. When either <c>HasKitchen</c> or
/// <c>HasTables</c> transitions from <c>false → true</c>, <c>BranchService</c>
/// runs the matching feature gate (<c>KdsBasic</c> / <c>TableService</c>) and
/// may return <c>402 Payment Required</c>.
/// </summary>
public class UpdateBranchRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? LocationName { get; set; }

    public bool? HasKitchen { get; set; }

    public bool? HasTables { get; set; }
}

/// <summary>
/// Runtime-level branch config update (Name and location only). Consumed by
/// <c>PUT /api/branch/{id}/config</c>. Kitchen/tables toggles are explicitly
/// NOT accepted here — use <c>PATCH /api/branch/{id}/settings</c> instead.
/// </summary>
public class UpdateBranchConfigRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? LocationName { get; set; }
}

public class VerifyPinRequest
{
    [Required]
    public string Pin { get; set; } = null!;
}

public class UpdatePinRequest
{
    [Required]
    public string CurrentPin { get; set; } = null!;

    [Required]
    public string NewPin { get; set; } = null!;
}

/// <summary>
/// Request body for creating a new branch.
/// </summary>
public class CreateBranchRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? LocationName { get; set; }
}

/// <summary>
/// Request body for copying a catalog from another branch.
/// </summary>
public class CopyCatalogRequest
{
    /// <summary>
    /// The source branch ID to copy the catalog from.
    /// </summary>
    [Required]
    public int SourceBranchId { get; set; }
}

public class CreateBusinessRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required]
    public int PlanTypeId { get; set; }

    /// <summary>
    /// Macro category id (1-4) that drives POS experience, plan rules and Stripe pricing.
    /// Hardened by BDD-015 — the endpoint used to default silently to Retail; now required
    /// to match the invariant already enforced by <c>/api/auth/register</c>.
    /// </summary>
    [Required]
    [Range(1, 4)]
    public int PrimaryMacroCategoryId { get; set; }
}

/// <summary>
/// Flat settings projection consumed by the frontend Settings screen.
/// Combines <see cref="POS.Domain.Models.Business.Name"/> with the matrix
/// branch's <c>Address</c> and <c>Phone</c>.
/// </summary>
public class BusinessSettingsDto
{
    public string BusinessName { get; set; } = null!;

    public string? Address { get; set; }

    public string? Phone { get; set; }
}

/// <summary>
/// Request body for <c>PUT /api/business/settings</c>. Updates the business
/// display name and the matrix branch's contact information atomically.
/// </summary>
public class UpdateBusinessSettingsRequest
{
    [Required]
    [MaxLength(100)]
    public string BusinessName { get; set; } = null!;

    [MaxLength(300)]
    public string? Address { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }
}
