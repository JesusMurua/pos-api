using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Bridge;

/// <summary>
/// Live weight reading pushed by a scale connected to the local Fino Bridge.
/// Forwarded by the hub to the dashboard group only — the bridge that emitted
/// the event does not need to receive its own broadcast back.
/// </summary>
public class WeightPayloadDto
{
    /// <summary>Stable per-bridge identifier of the source scale.</summary>
    [Required]
    [MaxLength(100)]
    public string DeviceId { get; set; } = null!;

    /// <summary>
    /// Raw weight reading as the bridge formatted it. Kept as string so the
    /// cloud does not impose a unit/precision contract — the dashboard
    /// renders verbatim and the cashier app parses on its own terms.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string WeightData { get; set; } = null!;
}
