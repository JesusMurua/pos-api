using POS.Domain.DTOs.Customer;
using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Entitlement engine for the gym/services vertical. Inspects synced orders for
/// membership-bearing line items and creates the corresponding
/// <see cref="CustomerMembership"/> rows. The service does NOT call
/// <c>SaveChangesAsync</c>; the caller (typically <c>OrderService</c>) owns the
/// Unit-of-Work transaction and is responsible for committing.
/// </summary>
public interface IMembershipService
{
    /// <summary>
    /// Processes an order batch and stages new <see cref="CustomerMembership"/>
    /// rows for every membership-bearing item. Stacking is preserved by reading
    /// the latest <c>ValidUntil</c> from the beneficiary's existing Active rows
    /// for the same product and clamping the new <c>ValidFrom</c> to no earlier
    /// than <see cref="DateTime.UtcNow"/>.
    /// </summary>
    /// <param name="orders">Order batch to inspect (typically the batch saved by <c>OrderService.SyncOrdersAsync</c>).</param>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown for any of:
    ///   <list type="bullet">
    ///     <item><c>BENEFICIARY_REQUIRED</c> — membership item without a resolvable beneficiary customer id.</item>
    ///     <item><c>CROSS_TENANT_BENEFICIARY</c> — beneficiary belongs to a different business than the order's branch.</item>
    ///     <item><c>FROZEN_MEMBERSHIP_NOT_EXTENDABLE</c> — beneficiary already holds a Frozen membership for the same product.</item>
    ///   </list>
    /// </exception>
    Task ProcessOrderEntitlementsAsync(IEnumerable<Order> orders);

    /// <summary>
    /// Returns memberships expiring within the given window, scoped to the
    /// caller's tenant. Sorted by <c>ValidUntil</c> ascending so the closest
    /// expirations surface first. The repository handles the SQL projection;
    /// the service layer's responsibility is purely tenant-passthrough.
    /// </summary>
    /// <param name="callerBusinessId">Resolved by the controller from the JWT/auth context.</param>
    /// <param name="windowDays">Lookahead window in days. Caller is expected to clamp to a sane upper bound.</param>
    Task<IEnumerable<CustomerMembershipDto>> GetExpiringSoonAsync(int callerBusinessId, int windowDays);
}
