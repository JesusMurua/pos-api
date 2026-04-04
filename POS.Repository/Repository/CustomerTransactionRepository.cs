using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CustomerTransactionRepository : GenericRepository<CustomerTransaction>, ICustomerTransactionRepository
{
    public CustomerTransactionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<CustomerTransaction>> GetByCustomerAsync(
        int customerId, DateTime? from = null, DateTime? to = null)
    {
        var query = _context.CustomerTransactions
            .Where(t => t.CustomerId == customerId);

        if (from.HasValue)
            query = query.Where(t => t.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(t => t.CreatedAt <= to.Value);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
