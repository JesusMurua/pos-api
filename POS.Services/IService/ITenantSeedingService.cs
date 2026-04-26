namespace POS.Services.IService;

/// <summary>
/// Pre-populates a freshly created branch with a macro-shaped catalog (categories
/// and sample products) so newly registered tenants land on a non-empty POS.
/// Designed to run inside the existing register transaction so a seeding failure
/// rolls the whole signup back.
/// </summary>
public interface ITenantSeedingService
{
    /// <summary>
    /// Inserts the default categories and products for the given <paramref name="macroCategoryId"/>.
    /// Idempotent: if the branch already has any category, the call is a no-op so re-running
    /// (e.g. during re-registration or repair flows) never duplicates state. If no template
    /// exists for the macro, the call is also a no-op.
    /// </summary>
    Task SeedDefaultsForMacroAsync(int branchId, int macroCategoryId);
}
