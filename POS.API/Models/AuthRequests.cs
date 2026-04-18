using System.ComponentModel.DataAnnotations;

namespace POS.API.Models;

public class EmailLoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;
}

public class PinLoginRequest
{
    [Required]
    public int BranchId { get; set; }

    [Required]
    public string Pin { get; set; } = null!;
}

/// <summary>
/// Request body for switching the active branch.
/// </summary>
public class SwitchBranchRequest
{
    /// <summary>
    /// The target branch identifier to switch to.
    /// </summary>
    [Required]
    public int BranchId { get; set; }
}

/// <summary>
/// Request body for public registration.
/// </summary>
public class RegisterApiRequest
{
    [Required]
    [MaxLength(100)]
    public string BusinessName { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string OwnerName { get; set; } = null!;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = null!;

    /// <summary>
    /// List of sub-giro catalog IDs (BusinessTypeCatalog.Id) chosen at registration.
    /// At least one is required; the first entry drives the primary macro category.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<int> BusinessTypeIds { get; set; } = new();

    /// <summary>
    /// Optional description when the user selects "Otro" or a non-catalog giro.
    /// </summary>
    [MaxLength(100)]
    public string? CustomGiroDescription { get; set; }

    public int? PlanTypeId { get; set; }

    /// <summary>Folio prefix for the matrix branch (e.g., "ANB").</summary>
    [MaxLength(10)]
    public string? FolioPrefix { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "MX" if not provided.</summary>
    [MaxLength(2)]
    public string? CountryCode { get; set; }
}
