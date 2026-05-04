using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class TaxRepository : GenericRepository<Tax>, ITaxRepository
{
    public TaxRepository(ApplicationDbContext context) : base(context)
    {
    }
}
