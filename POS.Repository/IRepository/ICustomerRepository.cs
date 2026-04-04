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
}
