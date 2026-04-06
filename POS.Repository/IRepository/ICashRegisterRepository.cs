using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICashRegisterRepository : IGenericRepository<CashRegister>
{
    Task<IEnumerable<CashRegister>> GetByBranchAsync(int branchId);
    Task<CashRegister?> GetByDeviceUuidAsync(int branchId, string deviceUuid);
}
