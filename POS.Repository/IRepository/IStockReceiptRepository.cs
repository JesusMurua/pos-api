using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IStockReceiptRepository : IGenericRepository<StockReceipt>
{
    Task<IEnumerable<StockReceipt>> GetAllByBranchAsync(
        int branchId,
        int? supplierId = null,
        DateTime? from = null,
        DateTime? to = null);

    Task<StockReceipt?> GetWithItemsAsync(int id);
}
