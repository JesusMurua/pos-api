namespace POS.Domain.DTOs.Device;

public class DeviceResponse
{
    public int Id { get; set; }
    public string DeviceUuid { get; set; } = null!;
    public string Mode { get; set; } = null!;
    public string? Name { get; set; }
    public bool IsActive { get; set; }
    public int BranchId { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
