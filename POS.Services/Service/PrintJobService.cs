using Microsoft.Extensions.Logging;
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
    private readonly IPushNotificationService _pushService;
    private readonly ILogger<PrintJobService> _logger;

    public PrintJobService(
        IUnitOfWork unitOfWork,
        IPushNotificationService pushService,
        ILogger<PrintJobService> logger)
    {
        _unitOfWork = unitOfWork;
        _pushService = pushService;
        _logger = logger;
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

        // Best-effort push to the waiter who placed the order. Failures must NEVER
        // bubble up — the chef's KDS already saw a successful 200 from the PATCH.
        await NotifyWaiterAsync(job);

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

    #region Private Helper Methods

    /// <summary>
    /// Sends a "Comanda Lista" web push to the waiter that originated the order.
    /// Wrapped in try/catch so a missing subscription, expired endpoint, or VAPID
    /// gateway timeout cannot fail the parent MarkPrinted operation.
    /// </summary>
    private async Task NotifyWaiterAsync(PrintJob job)
    {
        try
        {
            var order = (await _unitOfWork.Orders.GetAsync(o => o.Id == job.OrderId)).FirstOrDefault();
            if (order?.UserId == null)
                return;

            var location = !string.IsNullOrWhiteSpace(order.TableName)
                ? $"la {order.TableName}"
                : "la orden para llevar";

            var destination = job.Destination switch
            {
                PrintingDestination.Kitchen => "Cocina",
                PrintingDestination.Bar     => "Barra",
                PrintingDestination.Waiters => "Meseros",
                _                           => job.Destination.ToString()
            };

            var title = "¡Comanda Lista! 🔔";
            var body  = $"La orden #{order.OrderNumber} de {location} ya está lista en {destination}.";

            await _pushService.SendToUserAsync(
                order.UserId.Value,
                title,
                body,
                new
                {
                    orderId = job.OrderId,
                    orderNumber = order.OrderNumber,
                    tableName = order.TableName,
                    destination = job.Destination.ToString()
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to push 'comanda lista' notification for print job {JobId} (order {OrderId})",
                job.Id, job.OrderId);
        }
    }

    #endregion
}
