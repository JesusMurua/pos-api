using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class KdsEventOutboxRepository : GenericRepository<KdsEventOutbox>, IKdsEventOutboxRepository
{
    public KdsEventOutboxRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<KdsEventOutbox>> GetPendingAsync(int batchSize)
    {
        return await _context.KdsEventOutbox
            .Where(e => !e.IsProcessed)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }
}
