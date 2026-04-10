using POS.Domain.Models;

namespace POS.Repository.IRepository;

/// <summary>
/// Repository for the KdsEventOutbox table. Used by the dispatcher worker
/// to fetch unprocessed events for broadcast to SignalR clients.
/// </summary>
public interface IKdsEventOutboxRepository : IGenericRepository<KdsEventOutbox>
{
    /// <summary>
    /// Returns unprocessed events ordered by CreatedAt for FIFO broadcast.
    /// </summary>
    Task<List<KdsEventOutbox>> GetPendingAsync(int batchSize);
}
