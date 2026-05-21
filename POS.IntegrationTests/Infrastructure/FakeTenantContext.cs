using POS.Domain.Interfaces;

namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Mutable <see cref="ITenantContext"/> for direct DbContext-level tests.
/// Constructed by hand and passed to <c>new ApplicationDbContext(options, fake)</c>
/// so a single test can pin the tenant identity without touching DI.
/// </summary>
internal sealed class FakeTenantContext : ITenantContext
{
    public int? BranchId { get; set; }
    public int? BusinessId { get; set; }
}
