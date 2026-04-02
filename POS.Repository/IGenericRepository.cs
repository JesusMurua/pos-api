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

    Task<PageData<T>> GetListPagedAsync(
        PageFilter pageFilter,
        Expression<Func<T, bool>>? filter = null);

    Task<T> AddAsync(T entity);

    Task AddRangeAsync(IEnumerable<T> entities);

    void Update(T entity);

    void Delete(T entity);
}
