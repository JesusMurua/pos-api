using POS.Domain.DTOs.Customer;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements the membership entitlement engine. Detects membership-bearing line
/// items in a batch of synced orders, validates beneficiary and tenant integrity,
/// and stages <see cref="CustomerMembership"/> rows on the Unit of Work without
/// calling <c>SaveChangesAsync</c>. The caller commits.
/// </summary>
public class MembershipService : IMembershipService
{
    private readonly IUnitOfWork _unitOfWork;

    public MembershipService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API

    /// <inheritdoc />
    public async Task ProcessOrderEntitlementsAsync(IEnumerable<Order> orders)
    {
        // ── Step 1: Filter to paid, non-cancelled orders with items ────────────
        var orderList = orders
            .Where(o => o.IsPaid && o.CancellationReason == null && o.Items != null && o.Items.Count > 0)
            .ToList();

        if (orderList.Count == 0) return;

        // ── Step 2: Batch-load every referenced Product (with typed Metadata) ──
        var productIds = orderList
            .SelectMany(o => o.Items!)
            .Select(i => i.ProductId)
            .Distinct()
            .ToList();

        if (productIds.Count == 0) return;

        var products = (await _unitOfWork.Products.GetAsync(p => productIds.Contains(p.Id)))
            .ToDictionary(p => p.Id);

        // ── Step 3: Batch-load referenced Branches for tenant validation ───────
        var branchIds = orderList.Select(o => o.BranchId).Distinct().ToList();
        var branches = (await _unitOfWork.Branches.GetAsync(b => branchIds.Contains(b.Id)))
            .ToDictionary(b => b.Id);

        // ── Step 4: Pre-pass — collect membership-eligible items ───────────────
        // Each pending entry already passed the typed Metadata, quantity, and
        // beneficiary validations. Cross-tenant + frozen checks happen later in a
        // single sweep once the customer + membership batches are loaded.
        var pending = new List<PendingEntitlement>();
        var beneficiaryIds = new HashSet<int>();

        foreach (var order in orderList)
        {
            foreach (var item in order.Items!)
            {
                if (!products.TryGetValue(item.ProductId, out var product)) continue;

                var durationDays = product.Metadata?.MembershipDurationDays;
                if (durationDays is null or <= 0) continue;

                if (item.Quantity <= 0) continue;

                var beneficiaryFromItem = item.Metadata?.BeneficiaryCustomerId;
                var beneficiaryId = (beneficiaryFromItem is > 0 ? beneficiaryFromItem : null) ?? order.CustomerId;

                if (!beneficiaryId.HasValue)
                    throw new ValidationException(
                        $"BENEFICIARY_REQUIRED: Order #{order.OrderNumber} sold membership product " +
                        $"'{product.Name}' without a beneficiary or payor customer.");

                pending.Add(new PendingEntitlement(order, item, product, durationDays.Value, beneficiaryId.Value));
                beneficiaryIds.Add(beneficiaryId.Value);
            }
        }

        if (pending.Count == 0) return;

        // ── Step 5: Batch-load beneficiary Customers ───────────────────────────
        var customers = (await _unitOfWork.Customers.GetAsync(c => beneficiaryIds.Contains(c.Id)))
            .ToDictionary(c => c.Id);

        // ── Step 6: Cross-tenant validation ────────────────────────────────────
        foreach (var entry in pending)
        {
            if (!customers.TryGetValue(entry.BeneficiaryId, out var customer))
                throw new ValidationException(
                    $"BENEFICIARY_NOT_FOUND: Customer {entry.BeneficiaryId} referenced by order " +
                    $"#{entry.Order.OrderNumber} item '{entry.Product.Name}' was not found.");

            if (!branches.TryGetValue(entry.Order.BranchId, out var branch))
                throw new ValidationException(
                    $"BRANCH_NOT_FOUND: Branch {entry.Order.BranchId} for order #{entry.Order.OrderNumber} was not found.");

            if (customer.BusinessId != branch.BusinessId)
                throw new ValidationException(
                    $"CROSS_TENANT_BENEFICIARY: Customer {entry.BeneficiaryId} does not belong to this business " +
                    $"(order #{entry.Order.OrderNumber}, item '{entry.Product.Name}').");
        }

        // ── Step 7: Batch-load existing Active and Frozen memberships ─────────
        // The (CustomerId, ProductId) pair is the natural lookup key. Loading
        // both Active and Frozen in one sweep avoids a second round-trip when
        // checking the freeze rule.
        var pendingPairs = pending
            .Select(p => new { p.BeneficiaryId, p.Product.Id })
            .Distinct()
            .ToList();

        var pendingCustomerIds = pendingPairs.Select(p => p.BeneficiaryId).Distinct().ToList();
        var pendingProductIds = pendingPairs.Select(p => p.Id).Distinct().ToList();

        var existing = (await _unitOfWork.CustomerMemberships.GetAsync(m =>
            pendingCustomerIds.Contains(m.CustomerId)
            && m.ProductId.HasValue
            && pendingProductIds.Contains(m.ProductId.Value)
            && (m.Status == MembershipStatus.Active || m.Status == MembershipStatus.Frozen)))
            .ToList();

        // ── Step 8: Per-item — frozen check, ValidFrom clamp, create row ──────
        var now = DateTime.UtcNow;

        foreach (var entry in pending)
        {
            var existingForPair = existing
                .Where(m => m.CustomerId == entry.BeneficiaryId && m.ProductId == entry.Product.Id)
                .ToList();

            // Frozen rule: if any frozen membership exists for this (customer, product),
            // no new entitlement can be issued until the freeze is lifted.
            if (existingForPair.Any(m => m.Status == MembershipStatus.Frozen))
                throw new ValidationException(
                    $"FROZEN_MEMBERSHIP_NOT_EXTENDABLE: Customer {entry.BeneficiaryId} holds a frozen " +
                    $"membership for product '{entry.Product.Name}' (order #{entry.Order.OrderNumber}).");

            // Stacking with backdate clamp: ValidFrom = max(latest active end, now).
            // Active rows whose ValidUntil already passed (lazy-expired per BDD §6.1.2)
            // contribute their date here, but the clamp prevents the new period from
            // starting in the past.
            var latestActiveEnd = existingForPair
                .Where(m => m.Status == MembershipStatus.Active)
                .Select(m => (DateTime?)m.ValidUntil)
                .DefaultIfEmpty(null)
                .Max();

            var validFrom = latestActiveEnd.HasValue && latestActiveEnd.Value > now
                ? latestActiveEnd.Value
                : now;

            var totalDays = entry.DurationDays * (double)entry.Item.Quantity;
            var validUntil = validFrom.AddDays(totalDays);

            var membership = new CustomerMembership
            {
                CustomerId = entry.BeneficiaryId,
                ProductId = entry.Product.Id,
                ValidFrom = validFrom,
                ValidUntil = validUntil,
                Status = MembershipStatus.Active,
                OriginatingOrderId = entry.Order.Id,
                CreatedAt = now
            };

            await _unitOfWork.CustomerMemberships.AddAsync(membership);
        }

        // ── Step 9: NO SaveChangesAsync here. The caller commits. Concurrency
        // (DbUpdateConcurrencyException → ConcurrencyConflictException) is
        // handled at the caller's commit boundary.
    }

    /// <inheritdoc />
    public Task<IEnumerable<CustomerMembershipDto>> GetExpiringSoonAsync(int callerBusinessId, int windowDays)
    {
        // Pure passthrough — repository owns the tenant-scoped SQL projection.
        // The controller is responsible for clamping windowDays to its policy
        // bounds (default 7, max 30) before delegating here.
        return _unitOfWork.CustomerMemberships.GetExpiringSoonAsync(callerBusinessId, windowDays);
    }

    #endregion

    #region Private Types

    /// <summary>
    /// Lightweight tuple for membership-eligible items, captured during the
    /// pre-pass to drive batch-loaded validation in subsequent steps.
    /// </summary>
    private sealed record PendingEntitlement(
        Order Order,
        OrderItem Item,
        Product Product,
        int DurationDays,
        int BeneficiaryId);

    #endregion
}
