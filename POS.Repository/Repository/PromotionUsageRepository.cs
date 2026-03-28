using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class PromotionUsageRepository : GenericRepository<PromotionUsage>, IPromotionUsageRepository
{
    public PromotionUsageRepository(ApplicationDbContext context) : base(context)
    {
    }
}
