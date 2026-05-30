using Microsoft.EntityFrameworkCore;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class BusinessRepository : GenericRepository<Business>, IBusinessRepository
{
    public BusinessRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Business> Items, int Total)> GetAllForAdminAsync(
        int pageNumber,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters() drops the BDD-019 tenant filter so the super-admin
        // surface can list every business regardless of the request's ITenantContext.
        var query = _context.Businesses
            .IgnoreQueryFilters()
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // .ToLower().Contains is portable between Npgsql (translates to
            // LOWER(...) LIKE ...) and the InMemory test provider (which does
            // not support EF.Functions.ILike). Acceptable for the current
            // tenant volume; revisit with an expression index if the directory
            // exceeds ~100k rows.
            var needle = search.ToLower();
            query = query.Where(b =>
                b.Name.ToLower().Contains(needle) ||
                b.Users!.Any(u => u.RoleId == UserRoleIds.Owner &&
                                   u.Email != null &&
                                   u.Email.ToLower().Contains(needle)));
        }

        var total = await query.CountAsync(cancellationToken);

        var skip = Math.Max(0, (pageNumber - 1) * pageSize);

        var items = await query
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Skip(skip)
            .Take(pageSize)
            // Filtered Include + Take limits the materialized owner set to at
            // most one row per business — avoids dragging the entire Users
            // collection for tenants with many staff users.
            .Include(b => b.Users!
                .Where(u => u.RoleId == UserRoleIds.Owner)
                .OrderBy(u => u.CreatedAt)
                .Take(1))
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
