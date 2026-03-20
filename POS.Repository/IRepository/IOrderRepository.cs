using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date);

    Task<IEnumerable<Order>> GetPendingSyncAsync();

    Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date);
}
