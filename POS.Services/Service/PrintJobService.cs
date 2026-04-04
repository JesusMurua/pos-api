using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements KDS-specific lifecycle transitions for <see cref="POS.Domain.Models.PrintJob"/> entities.
/// </summary>
public class PrintJobService : IPrintJobService
{
    private readonly IUnitOfWork _unitOfWork;

    public PrintJobService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <inheritdoc/>
    public async Task<bool> MarkAsInProgressAsync(int id, int branchId)
    {
        var job = await _unitOfWork.PrintJobs.GetByIdAsync(id);

        if (job == null || job.BranchId != branchId)
            return false;

        if (job.Status != PrintJobStatus.Pending)
            throw new ValidationException(
                $"Print job {id} cannot transition to InProgress from status '{job.Status}'. Only Pending jobs are eligible.");

        job.Status = PrintJobStatus.InProgress;
        _unitOfWork.PrintJobs.Update(job);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    #endregion
}
