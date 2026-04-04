using POS.Domain.Models;

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
}
