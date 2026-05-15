using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Bridge;

/// <summary>
/// Telemetry payload sent by the local Fino Bridge each time hardware (QR
/// scanner, biometric reader, manual override panel) evaluates an access
/// attempt. The bridge is the local authority for the decision; the backend
/// persists the audit row, decides nothing, and broadcasts the event to the
/// reception dashboard.
/// </summary>
public class ScanPayloadDto
{
    /// <summary>
    /// Customer identifier known by the bridge cache. Currently a numeric
    /// string (<see cref="POS.Domain.Models.Customer.Id"/>); the cloud parses
    /// it as int and verifies tenant ownership before accepting the row.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string Identifier { get; set; } = null!;

    /// <summary>
    /// Hardware channel that produced the scan. Mapped case-insensitively to
    /// <see cref="POS.Domain.Helpers.AccessMethodIds"/> by the hub.
    /// Recognised values: <c>"qr"</c>, <c>"biometric"</c>, anything else falls
    /// back to <c>Manual</c>.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Source { get; set; } = null!;

    /// <summary>True when the bridge granted access locally.</summary>
    public bool Authorized { get; set; }

    /// <summary>
    /// UTC timestamp the bridge recorded for the physical scan. Allows offline
    /// replays — the cloud accepts events up to 30 days in the past and clamps
    /// future timestamps (>5 min ahead) to the server's <c>UtcNow</c>.
    /// </summary>
    public DateTime ScanUtc { get; set; }

    /// <summary>
    /// Optional denial reason emitted by the bridge when <see cref="Authorized"/>
    /// is false. Must be one of the denial values in
    /// <see cref="POS.Domain.Helpers.AccessReasonIds"/>; unknown values fall
    /// back to <c>NoMembership</c> at the hub. Null is also acceptable.
    /// </summary>
    public int? DenialReasonId { get; set; }
}
