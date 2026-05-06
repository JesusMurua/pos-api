using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICustomerRepository : IGenericRepository<Customer>
{
    /// <summary>
    /// Gets all active customers for a business.
    /// </summary>
    Task<IEnumerable<Customer>> GetByBusinessAsync(int businessId);

    /// <summary>
    /// Searches customers by name, phone, or email within a business.
    /// </summary>
    Task<IEnumerable<Customer>> SearchAsync(int businessId, string query);

    /// <summary>
    /// Gets a customer by phone number within a business.
    /// </summary>
    Task<Customer?> GetByPhoneAsync(int businessId, string phone);

    /// <summary>
    /// Lightweight tenant-resolution lookup. Returns the <c>BusinessId</c> of the
    /// given customer without hydrating the full entity (avoids loading the
    /// strongly-typed <c>Metadata</c> and <c>ExtensionData</c> jsonb columns for
    /// what is effectively an ownership probe). Returns <c>null</c> when the
    /// customer does not exist.
    /// </summary>
    Task<int?> GetBusinessIdAsync(int customerId);
}
