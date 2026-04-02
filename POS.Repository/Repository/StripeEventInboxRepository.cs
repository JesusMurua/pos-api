using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class StripeEventInboxRepository : GenericRepository<StripeEventInbox>, IStripeEventInboxRepository
{
    public StripeEventInboxRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<StripeEventInbox>> GetPendingEventsAsync(int batchSize)
    {
        return await _context.StripeEventInbox
            .Where(e => e.Status == StripeEventStatus.Pending)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }
}
