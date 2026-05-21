using Microsoft.AspNetCore.Http;
using POS.Domain.Interfaces;

namespace POS.Repository.Tenancy;

/// <summary>
/// Resolves <see cref="ITenantContext"/> from the current request's JWT claims.
/// Claims (<c>branchId</c>, <c>businessId</c>) are read dynamically on every
/// property access — NOT cached in the constructor — because the surrounding
/// DbContext is scoped per request but authentication middleware may populate
/// <see cref="HttpContext.User"/> after the context graph is built.
/// Returns <c>null</c> when no HttpContext is bound or the claim is missing,
/// which causes the DbContext's tenant filters to degrade to a no-op
/// (background services, EF design-time tooling, migrations, seeding).
/// </summary>
public class HttpTenantContext : ITenantContext
{
    private const string BranchIdClaim = "branchId";
    private const string BusinessIdClaim = "businessId";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? BranchId => ReadIntClaim(BranchIdClaim);

    public int? BusinessId => ReadIntClaim(BusinessIdClaim);

    private int? ReadIntClaim(string claimType)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null) return null;

        var claim = user.FindFirst(claimType);
        if (claim is null) return null;

        return int.TryParse(claim.Value, out var value) ? value : null;
    }
}
