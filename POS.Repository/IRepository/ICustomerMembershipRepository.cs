using POS.Domain.DTOs.Customer;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

/// <summary>
/// Persistence contract for the <see cref="CustomerMembership"/> aggregate.
/// Inherits the generic CRUD surface; specialized read methods drive the
/// admin "Customer Detail → Memberships" panel.
/// </summary>
public interface ICustomerMembershipRepository : IGenericRepository<CustomerMembership>
{
    /// <summary>
    /// Returns the customer's memberships projected to <see cref="CustomerMembershipDto"/>
    /// and sorted by <c>ValidUntil</c> descending. Lazy-Expired rows (DB
    /// <c>Status = Active</c> but <c>ValidUntil &lt; UtcNow</c>) are projected
    /// with <c>Status = "Expired"</c> per BDD-019 §6.1.2 and respected by the
    /// optional <paramref name="status"/> filter.
    /// </summary>
    /// <param name="customerId">Owner of the memberships (caller must have validated tenant ownership).</param>
    /// <param name="status">
    /// Optional case-insensitive status filter (<c>Active</c>, <c>Expired</c>,
    /// <c>Frozen</c>, <c>Cancelled</c>). When null/empty/whitespace no filter is
    /// applied. Throws <see cref="POS.Domain.Exceptions.ValidationException"/>
    /// (<c>INVALID_STATUS</c>) for unknown values.
    /// </param>
    Task<IEnumerable<CustomerMembershipDto>> GetByCustomerAsync(int customerId, string? status);

    /// <summary>
    /// Returns memberships expiring within the given window for the caller's
    /// tenant — projected to <see cref="CustomerMembershipDto"/> and sorted by
    /// <c>ValidUntil</c> ascending (closest-to-expire first). Powers the Admin
    /// Dashboard "Expiring Soon" widget without any client-side iteration.
    /// </summary>
    /// <remarks>
    /// Filter contract:
    /// <list type="bullet">
    ///   <item>Tenant scoping: <c>m.Customer.BusinessId == businessId</c>.</item>
    ///   <item>Only stored <c>Status = Active</c>; lazy-Expired rows are
    ///         excluded automatically by the <c>ValidUntil &gt;= UtcNow</c>
    ///         floor. <c>Frozen</c> rows are excluded by design (paused clock).</item>
    ///   <item>Window: <c>UtcNow &lt;= ValidUntil &lt;= UtcNow + windowDays</c>.</item>
    /// </list>
    /// </remarks>
    Task<IEnumerable<CustomerMembershipDto>> GetExpiringSoonAsync(int businessId, int windowDays);

    /// <summary>
    /// Returns the customer's most recent membership entity (not DTO) ordered
    /// by <c>ValidUntil</c> descending with <c>CreatedAt</c> as deterministic
    /// tie-breaker. Powers the access-control "why was access denied?" branch:
    /// callers switch on the raw <see cref="POS.Domain.Enums.MembershipStatus"/>
    /// enum to distinguish Frozen, Cancelled, and Expired. Returns <c>null</c>
    /// when the customer has no membership history at all.
    /// </summary>
    Task<CustomerMembership?> GetLatestForCustomerAsync(int customerId);
}
