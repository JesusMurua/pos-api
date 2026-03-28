using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IZoneRepository : IGenericRepository<Zone>
{
    Task<IEnumerable<Zone>> GetByBranchAsync(int branchId);
}
