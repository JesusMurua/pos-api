using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class BusinessRepository : GenericRepository<Business>, IBusinessRepository
{
    public BusinessRepository(ApplicationDbContext context) : base(context)
    {
    }
}
