namespace POS.Domain.DTOs.Device;

/// <summary>
/// Back-office projection of a non-consumed, non-expired
/// <see cref="POS.Domain.Models.DeviceActivationCode"/> row. Surfaced via
/// <c>GET /api/device/pending-codes</c> so the Dashboard can render the live
/// codes that are currently consuming the device-licensing quota.
/// </summary>
public class PendingDeviceCodeDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Mode { get; set; } = null!;
    public int BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
