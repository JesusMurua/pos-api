using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Device;

public class DeviceRegistrationRequest
{
    [Required]
    [MaxLength(100)]
    public string DeviceUuid { get; set; } = null!;

    [Required]
    public int BranchId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Mode { get; set; } = null!;

    [MaxLength(100)]
    public string? Name { get; set; }
}
