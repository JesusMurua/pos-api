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

    /// <summary>
    /// Internal FK to the bound <c>Device</c> row.
    /// </summary>
    /// <remarks>
    /// DEPRECATED for client-facing logic — prefer <see cref="DeviceUuid"/>.
    /// UUIDs survive Device-row replacements (e.g. re-pair after factory reset)
    /// while the integer FK does not. Kept for back-compat with consumers that
    /// already key off it. Scheduled for removal once all consumers migrate to
    /// <see cref="DeviceUuid"/>.
    /// </remarks>
    public int? DeviceId { get; set; }

    /// <summary>
    /// Authoritative linkage signal: <c>null</c> = register is unlinked,
    /// non-null = bound to the device with this UUID.
    /// </summary>
    /// <remarks>
    /// Mirror of <c>Device?.DeviceUuid</c>; both fields are populated
    /// atomically by the same EF query/projection. The Angular
    /// <c>CashRegister</c> interface keys off this flat field — it is the
    /// canonical signal for rendering the linkage badge and gating the
    /// "generate link code" action.
    /// </remarks>
    public string? DeviceUuid { get; set; }

    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Bound device, or <c>null</c> when the register has not been paired yet.
    /// </summary>
    /// <remarks>
    /// DEPRECATED — kept for back-compat with consumers built before the flat
    /// <see cref="DeviceUuid"/> was added. New consumers should read
    /// <see cref="DeviceUuid"/> directly; this nested object will be retired
    /// once the surface migration completes. Scheduled for removal once all
    /// consumers migrate to <see cref="DeviceUuid"/>.
    /// </remarks>
    public DeviceDto? Device { get; set; }
}
