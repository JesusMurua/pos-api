using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Device;

/// <summary>
/// Partial-update request for <c>PATCH /api/devices/{id}</c>. Both fields are
/// optional but at least one must be provided — the service enforces the
/// empty-body guard so the failure path is consistent with other tenant-safety
/// checks instead of relying on model binding.
/// </summary>
public class UpdateDeviceRequest
{
    /// <summary>
    /// New human-readable device label. Trimmed before persisting; blank after
    /// trim is rejected with 400.
    /// </summary>
    [MaxLength(100)]
    public string? Name { get; set; }

    /// <summary>
    /// Target branch id. Must belong to the caller's business and be active.
    /// Cross-business or inactive targets are rejected with 400.
    /// </summary>
    public int? BranchId { get; set; }
}
