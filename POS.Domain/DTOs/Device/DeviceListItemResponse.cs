namespace POS.Domain.DTOs.Device;

/// <summary>
/// Projection returned by the Back Office device list and by
/// <c>PATCH /api/devices/{id}</c>. Never carries <c>DeviceToken</c> — the long-lived
/// JWT is only issued at <c>POST /api/devices/register</c>, and re-exposing it on
/// an admin list would be a token-leakage vector.
/// </summary>
public class DeviceListItemResponse
{
    public int Id { get; set; }
    public string DeviceUuid { get; set; } = null!;
    public string? Name { get; set; }
    public string Mode { get; set; } = null!;
    public bool IsActive { get; set; }
    public int BranchId { get; set; }
    public string BranchName { get; set; } = null!;
    public DateTime? LastSeenAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
