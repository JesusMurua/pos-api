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

    public string? BusinessType { get; set; }

    /// <summary>
    /// List of business type codes for multi-giro support (e.g., ["Papeleria", "Abarrotes"]).
    /// Takes precedence over BusinessType when provided.
    /// </summary>
    public List<string>? BusinessTypes { get; set; }

    /// <summary>
    /// Optional description when the user selects "Otra tienda" or a custom giro.
    /// </summary>
    [MaxLength(100)]
    public string? CustomGiroDescription { get; set; }

    public string? PlanType { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "MX" if not provided.</summary>
    [MaxLength(2)]
    public string? CountryCode { get; set; }
}
