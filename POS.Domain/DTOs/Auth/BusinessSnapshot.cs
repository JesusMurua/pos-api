namespace POS.Domain.DTOs.Auth;

/// <summary>
/// Per-business operational counts surfaced inside <c>AuthResponse</c> so
/// the First-Run Experience welcome screen and the dashboard widgets can
/// render derived state (e.g. "you have 0 products — set up your first
/// one") without a per-metric round-trip. Counts are cross-branch — the
/// snapshot reflects the entire business, not the caller's current branch.
/// Populated by <see cref="POS.Services.IService.IAuthService"/> on every
/// flow that emits an <c>AuthResponse</c> (login, register, switch-branch,
/// session rehydrate, welcome-shown).
/// </summary>
/// <param name="UserCount">Active + inactive users belonging to the business.</param>
/// <param name="ProductCount">Products across every branch of the business.</param>
/// <param name="BranchCount">Branches owned by the business (including the matrix).</param>
/// <param name="TableCount">Restaurant tables across every branch.</param>
/// <param name="CashRegisterCount">Cash registers across every branch.</param>
/// <param name="DeviceCount">POS / KDS / kiosk devices across every branch.</param>
public sealed record BusinessSnapshot(
    int UserCount,
    int ProductCount,
    int BranchCount,
    int TableCount,
    int CashRegisterCount,
    int DeviceCount);
