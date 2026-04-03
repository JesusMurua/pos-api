using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class CashRegisterService : ICashRegisterService
{
    private readonly IUnitOfWork _unitOfWork;

    public CashRegisterService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets the current open session for a branch.
    /// </summary>
    public async Task<CashRegisterSession?> GetOpenSessionAsync(int branchId)
    {
        return await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);
    }

    /// <summary>
    /// Opens a new cash register session. Protected by a unique filtered index:
    /// only one open session per branch is allowed at DB level.
    /// </summary>
    public async Task<CashRegisterSession> OpenSessionAsync(int branchId, OpenSessionRequest request)
    {
        var existingSession = await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);
        if (existingSession != null)
            throw new ValidationException("There is already an open cash register session for this branch");

        var session = new CashRegisterSession
        {
            BranchId = branchId,
            OpenedBy = request.OpenedBy,
            InitialAmountCents = request.InitialAmountCents,
            Status = CashRegisterStatus.Open
        };

        await _unitOfWork.CashRegisterSessions.AddAsync(session);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique filtered index violation: another session was opened concurrently
            throw new ValidationException("There is already an open cash register session for this branch");
        }

        return session;
    }

    /// <summary>
    /// Closes the current open session with server-side financial calculations.
    /// Atomic: single transaction, concurrency-safe via xmin token.
    /// </summary>
    public async Task<CashRegisterSession> CloseSessionAsync(int branchId, CloseSessionRequest request)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        var session = await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);
        if (session == null)
            throw new NotFoundException("No open cash register session found for this branch");

        var closedAt = DateTime.UtcNow;

        // Calculate cash movements totals from the session's movements
        var totalCashInCents = session.Movements?
            .Where(m => m.Type == CashMovementType.In)
            .Sum(m => m.AmountCents) ?? 0;

        var totalCashOutCents = session.Movements?
            .Where(m => m.Type == CashMovementType.Out)
            .Sum(m => m.AmountCents) ?? 0;

        // Calculate cash sales: sum of Cash payments on orders for this branch during the session
        var cashSalesCents = await CalculateCashSalesAsync(branchId, session.OpenedAt, closedAt);

        var expectedAmountCents = session.InitialAmountCents + cashSalesCents + totalCashInCents - totalCashOutCents;

        session.Status = CashRegisterStatus.Closed;
        session.ClosedBy = request.ClosedBy;
        session.ClosedAt = closedAt;
        session.CountedAmountCents = request.CountedAmountCents;
        session.CashSalesCents = cashSalesCents;
        session.TotalCashInCents = totalCashInCents;
        session.TotalCashOutCents = totalCashOutCents;
        session.ExpectedAmountCents = expectedAmountCents;
        session.DifferenceCents = request.CountedAmountCents - expectedAmountCents;
        session.Notes = request.Notes;

        _unitOfWork.CashRegisterSessions.Update(session);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException(
                "The cash register session was modified by another user. Please refresh and try again.");
        }

        await transaction.CommitAsync();
        return session;
    }

    /// <summary>
    /// Adds a cash movement to the current open session.
    /// Bumps the parent session's UpdatedAt to trigger xmin concurrency protection,
    /// preventing phantom movements during a concurrent close.
    /// </summary>
    public async Task<CashMovement> AddMovementAsync(int branchId, AddMovementRequest request)
    {
        if (request.Type is not (CashMovementType.In or CashMovementType.Out))
            throw new ValidationException($"Movement type must be '{CashMovementType.In}' or '{CashMovementType.Out}'");

        if (request.AmountCents <= 0)
            throw new ValidationException("Amount must be greater than zero");

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        var session = await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);
        if (session == null)
            throw new NotFoundException("No open cash register session found for this branch");

        var movement = new CashMovement
        {
            SessionId = session.Id,
            Type = request.Type,
            AmountCents = request.AmountCents,
            Description = request.Description,
            CreatedBy = request.CreatedBy
        };

        await _unitOfWork.CashMovements.AddAsync(movement);

        // Bump parent session to trigger xmin update — prevents phantom movements
        // during a concurrent CloseSessionAsync
        session.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.CashRegisterSessions.Update(session);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException(
                "The cash register session was modified by another user. Please refresh and try again.");
        }

        await transaction.CommitAsync();
        return movement;
    }

    /// <summary>
    /// Gets cash register history for a date range.
    /// </summary>
    public async Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime from, DateTime to)
    {
        return await _unitOfWork.CashRegisterSessions.GetHistoryAsync(branchId, from, to);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Sums all Cash-method payments on orders for the given branch within the session window.
    /// </summary>
    private async Task<int> CalculateCashSalesAsync(int branchId, DateTime sessionOpenedAt, DateTime closedAt)
    {
        var cashPayments = await _unitOfWork.Orders.GetAsync(
            o => o.BranchId == branchId
                && o.CreatedAt >= sessionOpenedAt
                && o.CreatedAt <= closedAt
                && o.CancellationReason == null,
            "Payments");

        return cashPayments
            .SelectMany(o => o.Payments)
            .Where(p => p.Method == PaymentMethod.Cash)
            .Sum(p => p.AmountCents);
    }

    #endregion
}
