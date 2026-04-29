using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICashRegisterRepository : IGenericRepository<CashRegister>
{
    Task<IEnumerable<CashRegister>> GetByBranchAsync(int branchId);

    /// <summary>
    /// Resolves the bound register for a given device UUID via a single JOIN
    /// to <c>Devices</c>. The UUID is the only identifier the terminal knows
    /// at boot time (it lives in IndexedDB), so the public contract still
    /// accepts UUID even though the on-disk relationship is now <c>DeviceId</c>.
    /// Eager-loads <c>Device</c> so the caller can map the nested DTO without
    /// a follow-up query.
    /// </summary>
    Task<CashRegister?> GetByDeviceUuidAsync(int branchId, string deviceUuid);

    /// <summary>
    /// Returns the register currently bound to <paramref name="deviceId"/> in
    /// <paramref name="branchId"/>, or <c>null</c>. Used by the takeover flow
    /// to detect collisions on the unique partial index before reassigning.
    /// </summary>
    Task<CashRegister?> GetByDeviceIdAsync(int branchId, int deviceId);

    /// <summary>
    /// Tracked fetch by id with the <c>Device</c> navigation eager-loaded so
    /// service code can map straight to <c>CashRegisterDto</c>.
    /// </summary>
    Task<CashRegister?> GetByIdWithDeviceAsync(int id);

    /// <summary>
    /// Returns the register matching a given name within a branch, or null.
    /// Used by the takeover/recovery flow to detect name collisions.
    /// </summary>
    Task<CashRegister?> GetByNameAsync(int branchId, string name);
}
