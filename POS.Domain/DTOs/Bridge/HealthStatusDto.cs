namespace POS.Domain.DTOs.Bridge;

/// <summary>
/// Periodic heartbeat / health snapshot pushed by the bridge. The hub uses it
/// to bump <c>Device.LastSeenAt</c> and emits a structured information log
/// with the rest of the telemetry for operational visibility.
/// </summary>
public class HealthStatusDto
{
    /// <summary>True when the bridge currently has a healthy outbound connection to the cloud.</summary>
    public bool IsCloudConnected { get; set; }

    /// <summary>Number of physical peripherals (scanners, scales, etc.) configured on the bridge at report time.</summary>
    public int ConfiguredDevicesCount { get; set; }

    /// <summary>UTC timestamp of the bridge's last successful sync down from the cloud.</summary>
    public DateTime LastSyncUtc { get; set; }
}
