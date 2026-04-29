using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IDeviceActivationCodeRepository : IGenericRepository<DeviceActivationCode>
{
    Task<DeviceActivationCode?> GetByCodeAsync(string code);
    Task<bool> CodeExistsAsync(string code);

    /// <summary>
    /// Fetches the activation row for <paramref name="code"/> with a
    /// PostgreSQL <c>SELECT ... FOR UPDATE</c> row-level lock. Must only be
    /// called inside an open transaction; the lock is released on commit or
    /// rollback. Concurrent callers serialize on the same row, eliminating
    /// the race where two terminals consume a single code.
    /// <para>
    /// Navigations <c>Business</c> and <c>Branch</c> are explicitly loaded
    /// after the locked fetch (raw SQL cannot be safely composed with
    /// <c>Include</c> without breaking the lock semantics).
    /// </para>
    /// </summary>
    Task<DeviceActivationCode?> GetByCodeForUpdateAsync(string code);
}
