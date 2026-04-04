using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class FiscalCustomerRepository : GenericRepository<FiscalCustomer>, IFiscalCustomerRepository
{
    public FiscalCustomerRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<FiscalCustomer?> GetByRfcAsync(int businessId, string rfc)
    {
        return await _context.FiscalCustomers
            .FirstOrDefaultAsync(c => c.BusinessId == businessId
                && c.Rfc == rfc.ToUpperInvariant());
    }

    public async Task<IEnumerable<FiscalCustomer>> GetByBusinessAsync(int businessId)
    {
        return await _context.FiscalCustomers
            .Where(c => c.BusinessId == businessId)
            .OrderBy(c => c.BusinessName)
            .ToListAsync();
    }
}
