using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CustomerRepository : GenericRepository<Customer>, ICustomerRepository
{
    public CustomerRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Customer>> GetByBusinessAsync(int businessId)
    {
        return await _context.Customers
            .Where(c => c.BusinessId == businessId && c.IsActive)
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .ToListAsync();
    }

    public async Task<IEnumerable<Customer>> SearchAsync(int businessId, string query)
    {
        var lowerQuery = query.ToLowerInvariant();

        return await _context.Customers
            .Where(c => c.BusinessId == businessId
                && c.IsActive
                && (c.FirstName.ToLower().Contains(lowerQuery)
                    || (c.LastName != null && c.LastName.ToLower().Contains(lowerQuery))
                    || (c.Phone != null && c.Phone.Contains(lowerQuery))
                    || (c.Email != null && c.Email.ToLower().Contains(lowerQuery))))
            .OrderBy(c => c.LastName).ThenBy(c => c.FirstName)
            .Take(20)
            .ToListAsync();
    }

    public async Task<Customer?> GetByPhoneAsync(int businessId, string phone)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.BusinessId == businessId && c.Phone == phone);
    }
}
