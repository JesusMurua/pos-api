namespace POS.Services.IService;

/// <summary>
/// Provides KDS-specific operations on <see cref="POS.Domain.Models.PrintJob"/> entities.
/// State-transition methods used exclusively by the KDS tablet to update job lifecycle.
/// </summary>
public interface IPrintJobService
{
    /// <summary>
    /// Transitions a print job from <c>Pending</c> to <c>InProgress</c>.
    /// Signals that a KDS operator acknowledged the ticket and is actively preparing the items.
    /// </summary>
    /// <param name="id">Primary key of the print job.</param>
    /// <param name="branchId">Branch scope — prevents cross-branch writes.</param>
    /// <returns>
    /// <c>true</c> if the transition succeeded;
    /// <c>false</c> if the job was not found or does not belong to <paramref name="branchId"/>.
    /// </returns>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when the job is not in <c>Pending</c> status.
    /// </exception>
    Task<bool> MarkAsInProgressAsync(int id, int branchId);
}
