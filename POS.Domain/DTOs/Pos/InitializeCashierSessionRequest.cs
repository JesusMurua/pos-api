using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Pos;

/// <summary>
/// Payload for <c>POST /api/Pos/initialize-cashier-session</c>. The browser
/// supplies its locally-persisted <see cref="DeviceUuid"/>; the server
/// orchestrates device registration + register create-or-takeover + link in
/// a single transaction so a partial failure cannot leave the tenant with
/// an orphan register or a register pointing at a stale device id.
/// </summary>
public sealed record InitializeCashierSessionRequest
{
    /// <summary>
    /// Stable per-terminal identifier generated client-side (typically a v4
    /// UUID persisted in IndexedDB). Same value re-pairs the same browser
    /// idempotently.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string DeviceUuid { get; init; } = null!;

    /// <summary>
    /// Display name of the register, normalized to lowercase before lookup
    /// to keep the unique <c>(BranchId, Name)</c> index case-insensitive.
    /// Defaults to <c>"Caja Principal"</c> when null.
    /// </summary>
    [MaxLength(50)]
    public string? RegisterName { get; init; }

    /// <summary>
    /// Display name persisted on <c>Device.Name</c> so the back-office
    /// surface can render a human-readable label. Defaults to
    /// <c>"Caja Web"</c> when null.
    /// </summary>
    [MaxLength(100)]
    public string? DeviceName { get; init; }

    /// <summary>
    /// Optional branch override. When null, the JWT <c>branchId</c> claim is
    /// used. Owner/admin callers can target any branch of their business
    /// without a prior <c>SwitchBranch</c>; Manager callers must additionally
    /// have a <c>UserBranches</c> assignment to the requested branch.
    /// Cross-tenant overrides return <c>404</c> (same response as a branch
    /// that does not exist) to avoid leaking branch existence.
    /// </summary>
    public int? BranchIdOverride { get; init; }

    /// <summary>
    /// When <c>false</c> (default), an existing register with a different
    /// bound device that has an open session returns <c>409</c> with the
    /// existing register and session ids so the SPA can prompt the
    /// operator. When <c>true</c>, the orchestration force-closes the prior
    /// session and reassigns the device in the same transaction.
    /// </summary>
    public bool Force { get; init; } = false;
}
