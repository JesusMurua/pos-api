using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IInventoryRepository : IGenericRepository<InventoryItem>
{
    Task<IEnumerable<InventoryItem>> GetAllByBranchAsync(int branchId);

    Task<IEnumerable<InventoryItem>> GetLowStockAsync(int branchId);

    Task<InventoryItem?> GetWithMovementsAsync(int id);
}
