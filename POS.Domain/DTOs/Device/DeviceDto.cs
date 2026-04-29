namespace POS.Domain.DTOs.Device;

/// <summary>
/// Lightweight device projection for nesting inside aggregate DTOs (e.g.
/// <c>CashRegisterDto</c>). Avoids round-trips when the consumer already has
/// the parent and just needs the device label/mode for display.
/// </summary>
public class DeviceDto
{
    public int Id { get; set; }
    public string DeviceUuid { get; set; } = null!;
    public string? Name { get; set; }
    public string Mode { get; set; } = null!;
    public bool IsActive { get; set; }
}
