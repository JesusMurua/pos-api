using Microsoft.EntityFrameworkCore;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class PaymentWebhookInboxRepository : GenericRepository<PaymentWebhookInbox>, IPaymentWebhookInboxRepository
{
    public PaymentWebhookInboxRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<List<PaymentWebhookInbox>> GetPendingEventsAsync(int batchSize)
    {
        return await _context.PaymentWebhookInbox
            .Where(e => e.Status == WebhookInboxStatus.Pending)
            .OrderBy(e => e.CreatedAt)
            .Take(batchSize)
            .ToListAsync();
    }
}
