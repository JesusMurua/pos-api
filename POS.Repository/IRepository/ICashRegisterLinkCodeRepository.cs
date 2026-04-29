using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICashRegisterLinkCodeRepository : IGenericRepository<CashRegisterLinkCode>
{
    /// <summary>
    /// Untracked existence check used by the collision-avoidance loop in the
    /// service layer. Returns <c>true</c> for any row matching <paramref name="code"/>,
    /// regardless of <c>IsUsed</c> or expiration — the unique index guards the
    /// full code space, not just the live subset.
    /// </summary>
    Task<bool> CodeExistsAsync(string code);

    /// <summary>
    /// Fetches the link-code row for <paramref name="code"/> with a PostgreSQL
    /// <c>SELECT ... FOR UPDATE</c> row-level lock. Must only be called inside
    /// an open transaction; the lock is released on commit or rollback.
    /// Concurrent callers serialize on the same row, eliminating the race
    /// where two terminals consume a single code.
    /// <para>
    /// The <c>CashRegister</c> navigation is explicitly loaded after the locked
    /// fetch (raw SQL cannot be safely composed with <c>Include</c> without
    /// breaking the lock semantics).
    /// </para>
    /// </summary>
    Task<CashRegisterLinkCode?> GetByCodeForUpdateAsync(string code);
}
