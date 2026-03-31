using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ISupplierRepository : IGenericRepository<Supplier>
{
    Task<IEnumerable<Supplier>> GetAllByBranchAsync(int branchId);

    Task<Supplier?> GetWithReceiptsAsync(int id);
}
