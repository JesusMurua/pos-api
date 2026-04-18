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
    /// Macro category chosen at registration — drives POS experience, plan rules and pricing.
    /// Sub-giros and <c>CustomGiroDescription</c> are captured later during onboarding via
    /// <c>PUT /api/business/giro</c>.
    /// </summary>
    [Required]
    [Range(1, 4)]
    public int PrimaryMacroCategoryId { get; set; }

    public int? PlanTypeId { get; set; }

    /// <summary>Folio prefix for the matrix branch (e.g., "ANB").</summary>
    [MaxLength(10)]
    public string? FolioPrefix { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "MX" if not provided.</summary>
    [MaxLength(2)]
    public string? CountryCode { get; set; }
}
