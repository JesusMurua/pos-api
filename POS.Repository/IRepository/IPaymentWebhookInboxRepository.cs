using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IPaymentWebhookInboxRepository : IGenericRepository<PaymentWebhookInbox>
{
    /// <summary>
    /// Returns pending events ordered by CreatedAt for sequential processing.
    /// </summary>
    Task<List<PaymentWebhookInbox>> GetPendingEventsAsync(int batchSize);
}
