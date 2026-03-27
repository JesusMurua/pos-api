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
