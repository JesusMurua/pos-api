using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class BranchRepository : GenericRepository<Branch>, IBranchRepository
{
    public BranchRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Branch?> GetByIdWithConfigAsync(int branchId)
    {
        return await _context.Branches
            .Include(b => b.Business)
            .FirstOrDefaultAsync(b => b.Id == branchId);
    }

    public async Task<(int Counter, string? Prefix, string? Format)> IncrementFolioCounterAsync(int branchId)
    {
        var result = await _context.Database
            .SqlQuery<int>($@"
                UPDATE ""Branches""
                SET ""FolioCounter"" = ""FolioCounter"" + 1
                WHERE ""Id"" = {branchId}
                RETURNING ""FolioCounter""")
            .ToListAsync();

        var counter = result.FirstOrDefault();
        if (counter == 0)
            throw new InvalidOperationException($"Branch {branchId} not found");

        var branch = await _context.Branches
            .AsNoTracking()
            .Where(b => b.Id == branchId)
            .Select(b => new { b.FolioPrefix, b.FolioFormat })
            .FirstAsync();

        return (counter, branch.FolioPrefix, branch.FolioFormat);
    }
}
