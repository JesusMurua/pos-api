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

    #region Cash Register CRUD

    /// <summary>
    /// Gets all cash registers for a branch.
    /// </summary>
    public async Task<IEnumerable<CashRegister>> GetAllRegistersAsync(int branchId)
    {
        return await _unitOfWork.CashRegisters.GetByBranchAsync(branchId);
    }

    /// <summary>
    /// Creates a new cash register for a branch.
    /// </summary>
    public async Task<CashRegister> CreateRegisterAsync(int branchId, CreateCashRegisterRequest request)
    {
        var register = new CashRegister
        {
            BranchId = branchId,
            Name = request.Name,
            DeviceUuid = request.DeviceUuid
        };

        await _unitOfWork.CashRegisters.AddAsync(register);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException(
                "A cash register with that name or device UUID already exists for this branch.");
        }

        return register;
    }

    /// <summary>
    /// Updates a cash register's name and/or device UUID.
    /// </summary>
    public async Task<CashRegister> UpdateRegisterAsync(int registerId, int branchId, UpdateCashRegisterRequest request)
    {
        var register = await _unitOfWork.CashRegisters.GetByIdAsync(registerId)
            ?? throw new NotFoundException($"Cash register with id {registerId} not found");

        if (register.BranchId != branchId)
            throw new UnauthorizedException("Cash register does not belong to this branch");

        register.Name = request.Name;
        register.DeviceUuid = request.DeviceUuid;

        _unitOfWork.CashRegisters.Update(register);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException(
                "A cash register with that name or device UUID already exists for this branch.");
        }

        return register;
    }

    /// <summary>
    /// Toggles a cash register's active status.
    /// </summary>
    public async Task<CashRegister> ToggleRegisterAsync(int registerId, int branchId)
    {
        var register = await _unitOfWork.CashRegisters.GetByIdAsync(registerId)
            ?? throw new NotFoundException($"Cash register with id {registerId} not found");

        if (register.BranchId != branchId)
            throw new UnauthorizedException("Cash register does not belong to this branch");

        if (register.IsActive)
        {
            var openSession = await _unitOfWork.CashRegisterSessions.GetOpenSessionByRegisterAsync(registerId);
            if (openSession != null)
                throw new ValidationException(
                    "Cannot deactivate a cash register with an open session. Close the session first.");
        }

        register.IsActive = !register.IsActive;
        _unitOfWork.CashRegisters.Update(register);
        await _unitOfWork.SaveChangesAsync();

        return register;
    }

    /// <summary>
    /// Gets a cash register by its bound device UUID.
    /// </summary>
    public async Task<CashRegister?> GetRegisterByDeviceUuidAsync(int branchId, string deviceUuid)
    {
        return await _unitOfWork.CashRegisters.GetByDeviceUuidAsync(branchId, deviceUuid);
    }

    #endregion

    #region Session Operations

    /// <summary>
    /// Gets the current open session for a specific register, or for the branch (legacy).
    /// If cashRegisterId is provided, fetches by register. Otherwise, fetches by branch.
    /// </summary>
    public async Task<CashRegisterSession?> GetOpenSessionAsync(int branchId, int? cashRegisterId = null)
    {
        if (cashRegisterId.HasValue)
            return await _unitOfWork.CashRegisterSessions.GetOpenSessionByRegisterAsync(cashRegisterId.Value);

        return await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);
    }

    /// <summary>
    /// Opens a new cash register session, optionally tied to a specific register.
    /// If CashRegisterId is provided, validates the register exists, belongs to the branch, and is active.
    /// Protected by unique filtered index at DB level.
    /// </summary>
    public async Task<CashRegisterSession> OpenSessionAsync(int branchId, OpenSessionRequest request)
    {
        if (request.CashRegisterId.HasValue)
        {
            var register = await _unitOfWork.CashRegisters.GetByIdAsync(request.CashRegisterId.Value)
                ?? throw new NotFoundException($"Cash register with id {request.CashRegisterId.Value} not found");

            if (register.BranchId != branchId)
                throw new UnauthorizedException("Cash register does not belong to this branch");

            if (!register.IsActive)
                throw new ValidationException("Cannot open a session on an inactive cash register.");

            var existingSession = await _unitOfWork.CashRegisterSessions
                .GetOpenSessionByRegisterAsync(request.CashRegisterId.Value);
            if (existingSession != null)
                throw new ValidationException("There is already an open session for this cash register.");
        }
        else
        {
            var existingSession = await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);
            if (existingSession != null)
                throw new ValidationException("There is already an open cash register session for this branch.");
        }

        var session = new CashRegisterSession
        {
            BranchId = branchId,
            CashRegisterId = request.CashRegisterId,
            OpenedBy = request.OpenedBy,
            InitialAmountCents = request.InitialAmountCents,
            CashRegisterStatusId = CashRegisterStatus.Open
        };

        await _unitOfWork.CashRegisterSessions.AddAsync(session);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException("There is already an open session for this cash register.");
        }

        return session;
    }

    /// <summary>
    /// Closes the current open session with server-side financial calculations.
    /// Atomic: single transaction, concurrency-safe via xmin token.
    /// </summary>
    public async Task<CashRegisterSession> CloseSessionAsync(int branchId, CloseSessionRequest request, int? cashRegisterId = null)
    {
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        var session = cashRegisterId.HasValue
            ? await _unitOfWork.CashRegisterSessions.GetOpenSessionByRegisterAsync(cashRegisterId.Value)
            : await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);

        if (session == null)
            throw new NotFoundException("No open cash register session found.");

        var closedAt = DateTime.UtcNow;

        var totalCashInCents = session.Movements?
            .Where(m => m.CashMovementTypeId == CashMovementType.In)
            .Sum(m => m.AmountCents) ?? 0;

        var totalCashOutCents = session.Movements?
            .Where(m => m.CashMovementTypeId == CashMovementType.Out)
            .Sum(m => m.AmountCents) ?? 0;

        var cashSalesCents = await CalculateCashSalesAsync(session.Id);

        var expectedAmountCents = session.InitialAmountCents + cashSalesCents + totalCashInCents - totalCashOutCents;

        session.CashRegisterStatusId = CashRegisterStatus.Closed;
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
    /// Adds a cash movement to the open session of a specific register, or for the branch (legacy).
    /// Bumps the parent session's UpdatedAt to trigger xmin concurrency protection.
    /// </summary>
    public async Task<CashMovement> AddMovementAsync(int branchId, AddMovementRequest request, int? cashRegisterId = null)
    {
        if (request.Type is not (CashMovementType.In or CashMovementType.Out))
            throw new ValidationException("Movement type must be 1 (In) or 2 (Out)");

        if (request.AmountCents <= 0)
            throw new ValidationException("Amount must be greater than zero");

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        var session = cashRegisterId.HasValue
            ? await _unitOfWork.CashRegisterSessions.GetOpenSessionByRegisterAsync(cashRegisterId.Value)
            : await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);

        if (session == null)
            throw new NotFoundException("No open cash register session found.");

        var movement = new CashMovement
        {
            SessionId = session.Id,
            CashMovementTypeId = request.Type,
            AmountCents = request.AmountCents,
            Description = request.Description,
            CreatedBy = request.CreatedBy
        };

        await _unitOfWork.CashMovements.AddAsync(movement);

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
    /// Gets cash register session history for a date range.
    /// </summary>
    public async Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime from, DateTime to)
    {
        return await _unitOfWork.CashRegisterSessions.GetHistoryAsync(branchId, from, to);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Sums all Cash-method payments on orders linked to a specific session via FK.
    /// Uses Order.CashRegisterSessionId instead of temporal window to guarantee
    /// exact precision with overlapping multi-till sessions.
    /// </summary>
    private async Task<int> CalculateCashSalesAsync(int sessionId)
    {
        var orders = await _unitOfWork.Orders.GetAsync(
            o => o.CashRegisterSessionId == sessionId
                && o.CancellationReason == null,
            "Payments");

        return orders
            .SelectMany(o => o.Payments)
            .Where(p => p.Method == PaymentMethod.Cash)
            .Sum(p => p.AmountCents);
    }

    #endregion
}
