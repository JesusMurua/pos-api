using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations on <see cref="PrintJob"/> entities:
/// querying, lifecycle transitions, and failure tracking.
/// </summary>
public interface IPrintJobService
{
    /// <summary>
    /// Returns all active (Pending/InProgress) print jobs for a branch,
    /// optionally filtered by destination.
    /// </summary>
    Task<IEnumerable<PrintJob>> GetPendingAsync(int branchId, PrintingDestination? destination);

    /// <summary>
    /// Returns all print jobs associated with a specific order.
    /// </summary>
    Task<IEnumerable<PrintJob>> GetByOrderAsync(string orderId);

    /// <summary>
    /// Transitions a print job from <c>Pending</c> to <c>InProgress</c>.
    /// </summary>
    /// <returns><c>true</c> if found and transitioned; <c>false</c> if not found.</returns>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when the job is not in <c>Pending</c> status.
    /// </exception>
    Task<bool> MarkAsInProgressAsync(int id, int branchId);

    /// <summary>
    /// Marks a print job as <c>Printed</c> with the current UTC timestamp.
    /// Valid from <c>Pending</c> or <c>InProgress</c>.
    /// </summary>
    /// <returns>The updated print job, or <c>null</c> if not found.</returns>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when the job is already in a terminal state.
    /// </exception>
    Task<PrintJob?> MarkPrintedAsync(int id, int branchId);

    /// <summary>
    /// Records a failed print attempt. Increments <c>AttemptCount</c> and transitions
    /// to <c>Failed</c> when max attempts is reached.
    /// </summary>
    /// <returns>The updated print job, or <c>null</c> if not found.</returns>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when the job is not in <c>Pending</c> status.
    /// </exception>
    Task<PrintJob?> MarkFailedAsync(int id, int branchId);
}
