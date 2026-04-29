using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICashRegisterSessionRepository : IGenericRepository<CashRegisterSession>
{
    Task<CashRegisterSession?> GetOpenSessionAsync(int branchId);
    Task<CashRegisterSession?> GetOpenSessionByRegisterAsync(int registerId);
    /// <summary>
    /// Returns cash register sessions whose <c>OpenedAt</c> falls in the half-open
    /// UTC range <c>[startUtc, endUtc)</c>. Both bounds must carry
    /// <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Tracked fetch by id with both user navigations eager-loaded. Used by
    /// the service layer immediately after closing a session, since the closed
    /// session no longer matches the "open" filters used by the other lookups.
    /// </summary>
    Task<CashRegisterSession?> GetByIdWithUsersAsync(int id);
}
