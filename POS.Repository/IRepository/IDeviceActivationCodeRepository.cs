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

    /// <summary>
    /// Counts pending (non-consumed, non-expired) activation codes of
    /// <paramref name="mode"/> within <paramref name="businessId"/>, optionally
    /// narrowed by <paramref name="branchId"/>. Used by the device-licensing
    /// engine to add the in-flight portion to the active hardware count.
    /// </summary>
    Task<int> CountPendingByModeAsync(int businessId, int? branchId, string mode);

    /// <summary>
    /// Returns the pending (non-consumed) activation codes that match the
    /// idempotency key <c>(BranchId, Mode, Name)</c>. Used by the hygiene
    /// step of <c>GenerateActivationCodeAsync</c> to invalidate any prior
    /// code with the same intended target before issuing a new one.
    /// </summary>
    Task<IReadOnlyList<DeviceActivationCode>> GetPendingByTargetAsync(int branchId, string mode, string name);

    /// <summary>
    /// Lists every non-consumed, non-expired activation code owned by
    /// <paramref name="businessId"/>, optionally narrowed by
    /// <paramref name="branchId"/>. The <c>Branch</c> navigation is loaded so
    /// the service layer can map <c>BranchName</c> without an extra round-trip.
    /// Tenancy filter uses the row's own <c>BusinessId</c> column (no join),
    /// matching the pattern used by <see cref="CountPendingByModeAsync"/>.
    /// </summary>
    Task<IReadOnlyList<DeviceActivationCode>> ListPendingByBusinessAsync(int businessId, int? branchId = null);
}
