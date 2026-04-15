using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using POS.Domain.Interfaces;

namespace POS.Repository.Interceptors;

/// <summary>
/// Zero-trust tenancy guard: on every insert, overwrites BranchId on entities
/// implementing <see cref="IBranchScoped"/> with the value from the current
/// JWT "branchId" claim. The client-provided value is always discarded to
/// prevent cross-tenant injection.
/// Skips silently when no HttpContext is available (background jobs, seeding,
/// migrations) or when the claim is absent.
/// </summary>
public class BranchInjectionInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public BranchInjectionInterceptor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyBranchInjection(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ApplyBranchInjection(eventData);
        return base.SavingChanges(eventData, result);
    }

    private void ApplyBranchInjection(DbContextEventData eventData)
    {
        if (eventData.Context is null) return;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null) return;

        var claim = httpContext.User.FindFirst("branchId");
        if (claim is null || !int.TryParse(claim.Value, out var branchId)) return;

        var entries = eventData.Context.ChangeTracker
            .Entries<IBranchScoped>()
            .Where(e => e.State == EntityState.Added);

        foreach (var entry in entries)
        {
            entry.Entity.BranchId = branchId;
        }
    }
}
