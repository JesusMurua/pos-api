namespace POS.Domain.DTOs.Device;

/// <summary>
/// Per-mode quota snapshot for a single device class (cashier, kitchen, kiosk,
/// reception, tables). Returned inside <see cref="DeviceLimitsDto"/> so the
/// frontend can render disabled-state UI per mode without probing each
/// activation endpoint reactively.
/// </summary>
public class DeviceModeQuotaDto
{
    /// <summary>Canonical mode code from <c>DeviceModeCodes</c>.</summary>
    public string Mode { get; set; } = null!;

    /// <summary>
    /// Counting scope for this mode: <c>"Business"</c> (Global) or <c>"Branch"</c>.
    /// Cashier/Tables/Kitchen/Kiosk are per-business; Reception is per-branch.
    /// </summary>
    public string Scope { get; set; } = null!;

    /// <summary>Active devices currently registered (<c>IsActive=true</c>) under this mode within the relevant scope.</summary>
    public int ActiveDevices { get; set; }

    /// <summary>Pending activation codes (not consumed, not expired) that count against the same quota.</summary>
    public int PendingCodes { get; set; }

    /// <summary>Combined consumption: <c>ActiveDevices + PendingCodes</c>. This is the value the enforcer compares against the limit.</summary>
    public int Usage { get; set; }

    /// <summary>Plan-derived limit for this mode. <c>null</c> means unlimited (e.g. Enterprise).</summary>
    public int? PlanLimit { get; set; }

    /// <summary>Sum of Stripe add-on quantities currently licensed for this feature (active/trialing only).</summary>
    public int AddonLimit { get; set; }

    /// <summary>
    /// Effective limit = <c>PlanLimit + AddonLimit</c>. <c>null</c> when the
    /// plan limit is unlimited; the frontend should render "∞" in that case.
    /// </summary>
    public int? EffectiveLimit { get; set; }

    /// <summary>
    /// True iff <c>Usage &gt;= EffectiveLimit</c>. Always <c>false</c> when
    /// <see cref="IsUnlimited"/> is true. Frontend uses this to disable
    /// "Generate code" / "Activate device" actions for this mode.
    /// </summary>
    public bool IsLimitReached { get; set; }

    /// <summary>True when the plan grants unlimited devices of this mode (Enterprise tier).</summary>
    public bool IsUnlimited { get; set; }
}
