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

    /// <summary>
    /// Long-lived JWT issued for this device on registration. Infrastructure screens
    /// (KDS, Kiosk) persist it and use it to authenticate against HTTP and SignalR
    /// endpoints without a human user session. Null for update flows that do not
    /// mint a new token.
    /// </summary>
    public string? DeviceToken { get; set; }
}
