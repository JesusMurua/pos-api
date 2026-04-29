using POS.Domain.DTOs.Device;

namespace POS.Domain.DTOs.CashRegister;

/// <summary>
/// API response shape for a cash register. Replaces direct entity exposure
/// from the controller — keeps internal navigation properties out of the wire
/// format and folds the bound device into a nested object so the frontend
/// avoids a follow-up call.
/// </summary>
public class CashRegisterDto
{
    public int Id { get; set; }
    public int BranchId { get; set; }
    public string Name { get; set; } = null!;
    public int? DeviceId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Bound device, or <c>null</c> when the register has not been paired yet.</summary>
    public DeviceDto? Device { get; set; }
}
