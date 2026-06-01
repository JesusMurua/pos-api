using System.Linq.Expressions;
using POS.Repository.Utils;

namespace POS.Repository;

public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);

    Task<IEnumerable<T>> GetAllAsync();

    Task<IEnumerable<T>> GetAsync(
        Expression<Func<T, bool>>? filter = null,
        string? includeProperties = null);

    /// <summary>
    /// Returns <c>true</c> when at least one row matches <paramref name="filter"/>.
    /// Translates to a single <c>SELECT EXISTS(...)</c> at the SQL layer — prefer
    /// over <c>GetAsync(...).Any()</c> on hot paths to avoid materializing rows
    /// when only the boolean answer is needed.
    /// </summary>
    Task<bool> AnyAsync(Expression<Func<T, bool>> filter);

    /// <summary>
    /// Counts rows matching <paramref name="filter"/>. Used by
    /// <c>AuthResponse.Snapshot</c> for business-scoped entities (User, Branch)
    /// whose tenant filter naturally aligns with the count predicate. Branch-
    /// scoped entities use their own <c>CountForBusinessAsync</c> instead
    /// because their global query filter contradicts a business-wide total.
    /// </summary>
    Task<int> CountAsync(Expression<Func<T, bool>> filter);

    Task<PageData<T>> GetListPagedAsync(
        PageFilter pageFilter,
        Expression<Func<T, bool>>? filter = null);

    Task<T> AddAsync(T entity);

    Task AddRangeAsync(IEnumerable<T> entities);

    void Update(T entity);

    void Delete(T entity);
}
