using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements print job querying and lifecycle transitions.
/// </summary>
public class PrintJobService : IPrintJobService
{
    private const int MaxAttempts = 3;

    private readonly IUnitOfWork _unitOfWork;

    public PrintJobService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <inheritdoc/>
    public async Task<IEnumerable<PrintJob>> GetPendingAsync(int branchId, PrintingDestination? destination)
    {
        return await _unitOfWork.PrintJobs.GetPendingByBranchAsync(branchId, destination);
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<PrintJob>> GetByOrderAsync(string orderId)
    {
        return await _unitOfWork.PrintJobs.GetByOrderAsync(orderId);
    }

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

    /// <inheritdoc/>
    public async Task<PrintJob?> MarkPrintedAsync(int id, int branchId)
    {
        var job = await _unitOfWork.PrintJobs.GetByIdAsync(id);

        if (job == null || job.BranchId != branchId)
            return null;

        if (job.Status != PrintJobStatus.Pending && job.Status != PrintJobStatus.InProgress)
            throw new ValidationException(
                $"Print job {id} is already in terminal status '{job.Status}'.");

        job.Status = PrintJobStatus.Printed;
        job.PrintedAt = DateTime.UtcNow;
        _unitOfWork.PrintJobs.Update(job);
        await _unitOfWork.SaveChangesAsync();

        return job;
    }

    /// <inheritdoc/>
    public async Task<PrintJob?> MarkFailedAsync(int id, int branchId)
    {
        var job = await _unitOfWork.PrintJobs.GetByIdAsync(id);

        if (job == null || job.BranchId != branchId)
            return null;

        if (job.Status != PrintJobStatus.Pending)
            throw new ValidationException(
                $"Print job {id} is already in status '{job.Status}'.");

        job.AttemptCount++;

        if (job.AttemptCount >= MaxAttempts)
            job.Status = PrintJobStatus.Failed;

        _unitOfWork.PrintJobs.Update(job);
        await _unitOfWork.SaveChangesAsync();

        return job;
    }

    #endregion
}
