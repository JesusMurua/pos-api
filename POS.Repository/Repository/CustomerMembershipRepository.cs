using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Customer;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

/// <summary>
/// Generic-backed repository for <see cref="CustomerMembership"/>. Specialized
/// query methods power the admin "Customer Detail → Memberships" panel.
/// </summary>
public class CustomerMembershipRepository : GenericRepository<CustomerMembership>, ICustomerMembershipRepository
{
    #region Constructor

    public CustomerMembershipRepository(ApplicationDbContext context) : base(context)
    {
    }

    #endregion

    #region Customer-scoped Reads

    /// <inheritdoc />
    public async Task<IEnumerable<CustomerMembershipDto>> GetByCustomerAsync(int customerId, string? status)
    {
        var now = DateTime.UtcNow;

        var query = _context.CustomerMemberships
            .AsNoTracking()
            .Where(m => m.CustomerId == customerId);

        // Compound filter — respects the lazy-Expired projection so that
        // querying ?status=Active excludes rows whose ValidUntil already passed,
        // and ?status=Expired includes those lazy-expired rows alongside the
        // explicitly Expired ones.
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<MembershipStatus>(status, ignoreCase: true, out var parsed))
                throw new ValidationException($"INVALID_STATUS: '{status}' is not a valid membership status.");

            query = parsed switch
            {
                MembershipStatus.Active =>
                    query.Where(m => m.Status == MembershipStatus.Active && m.ValidUntil >= now),
                MembershipStatus.Expired =>
                    query.Where(m => m.Status == MembershipStatus.Expired
                                  || (m.Status == MembershipStatus.Active && m.ValidUntil < now)),
                MembershipStatus.Frozen =>
                    query.Where(m => m.Status == MembershipStatus.Frozen),
                MembershipStatus.Cancelled =>
                    query.Where(m => m.Status == MembershipStatus.Cancelled),
                _ => query
            };
        }

        // Pure SQL projection: avoid .ToString() on the enum so EF emits a
        // CASE WHEN translatable to PostgreSQL. The lazy-Expired branch fires
        // before the regular Active branch.
        return await query
            .OrderByDescending(m => m.ValidUntil)
            .Select(m => new CustomerMembershipDto
            {
                Id = m.Id,
                ProductId = m.ProductId,
                ProductName = m.Product != null ? m.Product.Name : null,
                ValidFrom = m.ValidFrom,
                ValidUntil = m.ValidUntil,
                Status = m.Status == MembershipStatus.Active && m.ValidUntil < now
                            ? "Expired"
                            : m.Status == MembershipStatus.Active ? "Active"
                            : m.Status == MembershipStatus.Expired ? "Expired"
                            : m.Status == MembershipStatus.Frozen ? "Frozen"
                            : "Cancelled",
                OriginatingOrderId = m.OriginatingOrderId,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    #endregion
}
