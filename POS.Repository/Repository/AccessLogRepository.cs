using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class AccessLogRepository : GenericRepository<AccessLog>, IAccessLogRepository
{
    public AccessLogRepository(ApplicationDbContext context) : base(context)
    {
    }
}
