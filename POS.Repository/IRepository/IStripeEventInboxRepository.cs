using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IStripeEventInboxRepository : IGenericRepository<StripeEventInbox>
{
    /// <summary>
    /// Returns pending events ordered by CreatedAt for sequential processing.
    /// </summary>
    Task<List<StripeEventInbox>> GetPendingEventsAsync(int batchSize);
}
