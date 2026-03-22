using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICashRegisterSessionRepository : IGenericRepository<CashRegisterSession>
{
    Task<CashRegisterSession?> GetOpenSessionAsync(int branchId);
    Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime from, DateTime to);
}
