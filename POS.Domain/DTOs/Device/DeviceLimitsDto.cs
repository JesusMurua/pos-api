namespace POS.Domain.DTOs.Device;

/// <summary>
/// Aggregate per-mode device quota snapshot for a tenant. Exposed via
/// <c>GET /api/Device/limits</c> so the frontend can render proactive
/// disabled-state UI instead of relying on a reactive 403 from
/// <c>POST /api/Device/generate-code</c>. The list contains one entry per
/// metered device mode; modes that share a quota (e.g. Cashier and Tables
/// both consume <c>MaxCashRegisters</c>) appear as separate entries with
/// equal <c>EffectiveLimit</c> but per-mode <c>ActiveDevices</c>.
/// </summary>
public class DeviceLimitsDto
{
    public List<DeviceModeQuotaDto> Modes { get; set; } = new();
}
