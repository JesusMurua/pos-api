using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using POS.Repository.Utils;

namespace POS.Repository;

public class GenericRepository<T> : IGenericRepository<T> where T : class
{
    protected readonly ApplicationDbContext _context;
    protected readonly DbSet<T> _dbSet;

    public GenericRepository(ApplicationDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
    }

    public async Task<T?> GetByIdAsync(int id)
    {
        return await _dbSet.FindAsync(id);
    }

    public async Task<IEnumerable<T>> GetAllAsync()
    {
        return await _dbSet.ToListAsync();
    }

    public async Task<IEnumerable<T>> GetAsync(
        Expression<Func<T, bool>>? filter = null,
        string? includeProperties = null)
    {
        IQueryable<T> query = _dbSet;

        if (filter != null)
        {
            query = query.Where(filter);
        }

        if (!string.IsNullOrWhiteSpace(includeProperties))
        {
            foreach (var includeProperty in includeProperties.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                query = query.Include(includeProperty.Trim());
            }
        }

        return await query.ToListAsync();
    }

    public async Task<PageData<T>> GetListPagedAsync(
        PageFilter pageFilter,
        Expression<Func<T, bool>>? filter = null)
    {
        IQueryable<T> query = _dbSet;

        if (filter != null)
        {
            query = query.Where(filter);
        }

        var totalRows = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalRows / (double)pageFilter.PageSize);

        var data = await query
            .Skip((pageFilter.Page - 1) * pageFilter.PageSize)
            .Take(pageFilter.PageSize)
            .ToListAsync();

        return new PageData<T>
        {
            Data = data,
            RowsCount = totalRows,
            TotalPages = totalPages,
            CurrentPage = pageFilter.Page
        };
    }

    public async Task<T> AddAsync(T entity)
    {
        await _dbSet.AddAsync(entity);
        return entity;
    }

    public void Update(T entity)
    {
        _dbSet.Update(entity);
    }

    public void Delete(T entity)
    {
        _dbSet.Remove(entity);
    }
}
