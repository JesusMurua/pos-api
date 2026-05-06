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
}
