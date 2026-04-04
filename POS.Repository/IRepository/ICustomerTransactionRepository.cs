using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICustomerTransactionRepository : IGenericRepository<CustomerTransaction>
{
    /// <summary>
    /// Gets transactions for a customer, optionally filtered by date range.
    /// </summary>
    Task<IEnumerable<CustomerTransaction>> GetByCustomerAsync(int customerId, DateTime? from = null, DateTime? to = null);
}
