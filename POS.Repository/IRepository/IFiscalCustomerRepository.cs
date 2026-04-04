using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IFiscalCustomerRepository : IGenericRepository<FiscalCustomer>
{
    /// <summary>
    /// Gets a fiscal customer by RFC within a specific business.
    /// </summary>
    Task<FiscalCustomer?> GetByRfcAsync(int businessId, string rfc);

    /// <summary>
    /// Gets all fiscal customers for a business.
    /// </summary>
    Task<IEnumerable<FiscalCustomer>> GetByBusinessAsync(int businessId);
}
