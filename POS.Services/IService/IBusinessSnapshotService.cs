using POS.Domain.DTOs.Auth;

namespace POS.Services.IService;

/// <summary>
/// Builds the per-tenant operational <see cref="BusinessSnapshot"/>
/// surfaced inside <c>AuthResponse</c> and the admin detail view.
/// Extracted from <c>AuthService</c> so the snapshot can be reused by
/// non-auth code paths (e.g. <c>AdminBusinessesController.GetById</c>)
/// without forcing those callers to inject the entire auth surface.
/// </summary>
public interface IBusinessSnapshotService
{
    /// <summary>
    /// Issues six independent COUNT queries (two business-scoped, four
    /// branch-scoped via <c>CountForBusinessAsync</c> with
    /// <c>IgnoreQueryFilters</c>) and returns the aggregated snapshot.
    /// Cross-branch by construction — the totals reflect the entire
    /// business, not the caller's current branch.
    /// </summary>
    Task<BusinessSnapshot> BuildAsync(int businessId);
}
