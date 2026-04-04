using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

/// <summary>
/// EF Core implementation of <see cref="IPrintJobRepository"/>.
/// </summary>
public class PrintJobRepository : GenericRepository<PrintJob>, IPrintJobRepository
{
    public PrintJobRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<PrintJob>> GetPendingByBranchAsync(
        int branchId,
        PrintingDestination? destination)
    {
        var query = _context.PrintJobs
            .Where(j => j.BranchId == branchId && j.Status == PrintJobStatus.Pending);

        if (destination.HasValue)
            query = query.Where(j => j.Destination == destination.Value);

        return await query
            .OrderBy(j => j.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<PrintJob>> GetByOrderAsync(string orderId)
    {
        return await _context.PrintJobs
            .Where(j => j.OrderId == orderId)
            .OrderBy(j => j.Destination)
            .ThenBy(j => j.CreatedAt)
            .ToListAsync();
    }
}
