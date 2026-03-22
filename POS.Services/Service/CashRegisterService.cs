using POS.Domain.Exceptions;
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
    /// Opens a new cash register session.
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
            Status = "open"
        };

        var created = await _unitOfWork.CashRegisterSessions.AddAsync(session);
        await _unitOfWork.SaveChangesAsync();
        return created;
    }

    /// <summary>
    /// Closes the current open session.
    /// </summary>
    public async Task<CashRegisterSession> CloseSessionAsync(int branchId, CloseSessionRequest request)
    {
        var session = await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);

        if (session == null)
            throw new NotFoundException("No open cash register session found for this branch");

        session.Status = "closed";
        session.ClosedBy = request.ClosedBy;
        session.ClosedAt = DateTime.UtcNow;
        session.CountedAmountCents = request.CountedAmountCents;
        session.Notes = request.Notes;

        _unitOfWork.CashRegisterSessions.Update(session);
        await _unitOfWork.SaveChangesAsync();
        return session;
    }

    /// <summary>
    /// Adds a cash movement to the current open session.
    /// </summary>
    public async Task<CashMovement> AddMovementAsync(int branchId, AddMovementRequest request)
    {
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

        var created = await _unitOfWork.CashMovements.AddAsync(movement);
        await _unitOfWork.SaveChangesAsync();
        return created;
    }

    /// <summary>
    /// Gets cash register history for a date range.
    /// </summary>
    public async Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime from, DateTime to)
    {
        return await _unitOfWork.CashRegisterSessions.GetHistoryAsync(branchId, from, to);
    }

    #endregion
}
