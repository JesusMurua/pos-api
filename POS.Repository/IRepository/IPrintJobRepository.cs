using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

/// <summary>
/// Repository for <see cref="PrintJob"/> entities.
/// Extends the generic CRUD operations with polling and order-scoped queries
/// required by printers, KDS tablets, and the POS front-end.
/// </summary>
public interface IPrintJobRepository : IGenericRepository<PrintJob>
{
    /// <summary>
    /// Returns all <see cref="PrintJob"/>s in <see cref="PrintJobStatus.Pending"/> state
    /// for the given branch, ordered by <c>CreatedAt</c> ascending (FIFO).
    /// Used by peripheral devices polling for work to print.
    /// </summary>
    /// <param name="branchId">Branch to scope the query to.</param>
    /// <param name="destination">
    /// When provided, filters by a specific destination area (Kitchen, Bar, Waiters).
    /// When <c>null</c>, returns pending jobs for all destinations.
    /// </param>
    Task<IEnumerable<PrintJob>> GetPendingByBranchAsync(int branchId, PrintingDestination? destination);

    /// <summary>
    /// Returns all <see cref="PrintJob"/>s associated with a specific order,
    /// ordered by <c>Destination</c> then <c>CreatedAt</c>.
    /// Used by the POS front-end to show print status in the order detail view.
    /// </summary>
    /// <param name="orderId">The client UUID of the order (GUID string, 36 chars).</param>
    Task<IEnumerable<PrintJob>> GetByOrderAsync(string orderId);
}
