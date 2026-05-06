using POS.Domain.DTOs.Customer;
using POS.Domain.Models;
using POS.Repository.Utils;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing customers, store credit (fiado), and loyalty points.
/// </summary>
public interface ICustomerService
{
    /// <summary>
    /// Gets a customer by ID.
    /// </summary>
    Task<Customer> GetByIdAsync(int id);

    /// <summary>
    /// Gets all active customers for a business.
    /// </summary>
    Task<IEnumerable<Customer>> GetByBusinessAsync(int businessId);

    /// <summary>
    /// Searches customers by name, phone, or email within a business.
    /// </summary>
    Task<IEnumerable<Customer>> SearchAsync(int businessId, string query);

    /// <summary>
    /// Creates a new customer.
    /// </summary>
    Task<Customer> CreateAsync(int businessId, Customer customer);

    /// <summary>
    /// Updates an existing customer's profile data.
    /// </summary>
    Task<Customer> UpdateAsync(int id, Customer customer);

    /// <summary>
    /// Deactivates a customer (soft delete).
    /// </summary>
    Task DeactivateAsync(int id);

    /// <summary>
    /// Adds credit to a customer's balance (customer pays down their tab).
    /// Creates a ledger entry of type AddCredit.
    /// </summary>
    Task<CustomerTransaction> AddCreditAsync(int customerId, int amountCents, string description, int branchId, string createdBy);

    /// <summary>
    /// Manual adjustment of credit balance by owner/manager.
    /// Creates a ledger entry of type CreditAdjustment.
    /// </summary>
    Task<CustomerTransaction> AdjustCreditAsync(int customerId, int amountCents, string description, int branchId, string createdBy);

    /// <summary>
    /// Manual adjustment of points balance by owner/manager.
    /// Creates a ledger entry of type PointsAdjustment.
    /// </summary>
    Task<CustomerTransaction> AdjustPointsAsync(int customerId, int pointsAmount, string description, int branchId, string createdBy);

    /// <summary>
    /// Gets transaction history for a customer with optional date filter.
    /// </summary>
    Task<IEnumerable<CustomerTransaction>> GetTransactionsAsync(int customerId, DateTime? from = null, DateTime? to = null);

    // ──────────────────────────────────────────
    // Phase 16 — Transactional operations
    // ──────────────────────────────────────────

    /// <summary>
    /// Consumes store credit (fiado) for an order payment.
    /// Creates a ledger entry of type UseCredit.
    /// Validates credit limit is not exceeded.
    /// </summary>
    Task<CustomerTransaction> UseCreditAsync(int customerId, int amountCents, string orderId, int branchId, string createdBy);

    /// <summary>
    /// Awards loyalty points based on order total and business config.
    /// Creates a ledger entry of type EarnPoints.
    /// Returns null if Business.LoyaltyEnabled is false (no-op).
    /// </summary>
    Task<CustomerTransaction?> EarnPointsAsync(int customerId, int orderTotalCents, string orderId, int branchId, string createdBy);

    /// <summary>
    /// Redeems loyalty points as payment. Returns the cent value of redeemed points.
    /// Creates a ledger entry of type RedeemPoints.
    /// Validates sufficient points balance and Business.LoyaltyEnabled.
    /// </summary>
    Task<(CustomerTransaction Transaction, int RedeemedValueCents)> RedeemPointsAsync(int customerId, int points, string orderId, int branchId, string createdBy);

    /// <summary>
    /// Recalculates denormalized balances from the transaction ledger (reconciliation).
    /// </summary>
    Task RecalculateBalancesAsync(int customerId);

    /// <summary>
    /// Links a CRM Customer to an existing FiscalCustomer.
    /// Validates both entities belong to the same business.
    /// </summary>
    Task LinkFiscalCustomerAsync(int customerId, int fiscalCustomerId);

    // ──────────────────────────────────────────
    // BDD-019 P4 — Customer-scoped read endpoints
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns paginated orders for the given customer scoped to the caller's
    /// tenant. Throws <see cref="POS.Domain.Exceptions.NotFoundException"/> when
    /// the customer is missing or belongs to a different business
    /// (information-hiding per BDD-019 audit A6).
    /// </summary>
    Task<PageData<CustomerOrderRowDto>> GetOrdersAsync(
        int customerId, int callerBusinessId, int page, int pageSize, DateTime? from, DateTime? to);

    /// <summary>
    /// Returns the customer's memberships sorted by <c>ValidUntil</c> desc.
    /// Optional <paramref name="status"/> filter accepts <c>Active</c>,
    /// <c>Expired</c> (includes lazy-expired), <c>Frozen</c>, or <c>Cancelled</c>.
    /// Throws <see cref="POS.Domain.Exceptions.NotFoundException"/> for
    /// cross-tenant access.
    /// </summary>
    Task<IEnumerable<CustomerMembershipDto>> GetMembershipsAsync(
        int customerId, int callerBusinessId, string? status);

    /// <summary>
    /// Returns aggregate spend / order count / last-order-at for the given
    /// customer. Computed via a single SQL aggregation. Returns zeros / null
    /// for customers without paid orders. Throws
    /// <see cref="POS.Domain.Exceptions.NotFoundException"/> for cross-tenant access.
    /// </summary>
    Task<CustomerStatsDto> GetStatsAsync(int customerId, int callerBusinessId);
}
