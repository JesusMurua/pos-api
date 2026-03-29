using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IBranchRepository : IGenericRepository<Branch>
{
    Task<Branch?> GetByIdWithConfigAsync(int branchId);

    /// <summary>
    /// Atomically increments folio counter and returns counter value + branch folio config.
    /// </summary>
    Task<(int Counter, string? Prefix, string? Format)> IncrementFolioCounterAsync(int branchId);
}
