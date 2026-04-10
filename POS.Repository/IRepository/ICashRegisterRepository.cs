using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICashRegisterRepository : IGenericRepository<CashRegister>
{
    Task<IEnumerable<CashRegister>> GetByBranchAsync(int branchId);
    Task<CashRegister?> GetByDeviceUuidAsync(int branchId, string deviceUuid);

    /// <summary>
    /// Returns the register matching a given name within a branch, or null.
    /// Used by the takeover/recovery flow to detect name collisions.
    /// </summary>
    Task<CashRegister?> GetByNameAsync(int branchId, string name);
}
