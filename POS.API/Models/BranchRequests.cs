using System.ComponentModel.DataAnnotations;

namespace POS.API.Models;

public class UpdateConfigRequest
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

public class CreateBusinessRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string PlanType { get; set; } = null!;
}
