using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IDiscountPresetRepository : IGenericRepository<DiscountPreset>
{
    Task<IEnumerable<DiscountPreset>> GetByBranchAsync(int branchId);
}
