using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.CashRegister;
using POS.Domain.DTOs.Device;
using POS.Domain.DTOs.User;
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
    public async Task<IEnumerable<CashRegisterDto>> GetAllRegistersAsync(int branchId)
    {
        var registers = await _unitOfWork.CashRegisters.GetByBranchAsync(branchId);
        return registers.Select(MapToDto);
    }

    /// <summary>
    /// Creates a new cash register for a branch, or — when <c>request.Takeover</c>
    /// is true — recovers an existing register with the same name by overwriting
    /// its bound device. This unblocks users who lost their local DeviceUuid (e.g.
    /// after clearing the browser cache) without orphaning the open session.
    /// </summary>
    public async Task<CashRegisterDto> CreateRegisterAsync(int branchId, CreateCashRegisterRequest request)
    {
        // Normalize name so "Caja", "caja " and "CAJA" collide on the unique index.
        request.Name = request.Name.Trim().ToLowerInvariant();

        // Resolve the incoming UUID to the strict DeviceId once. Cross-branch
        // UUIDs and unknown UUIDs both surface as a clean 404 instead of a
        // foreign-key violation at SaveChanges.
        var deviceId = await ResolveDeviceIdAsync(request.DeviceUuid, branchId);

        var existing = await _unitOfWork.CashRegisters.GetByNameAsync(branchId, request.Name);

        if (existing != null)
        {
            // Inactive registers were intentionally disabled by an admin —
            // recovery must NOT silently re-enable them.
            if (!existing.IsActive)
                throw new ValidationException("Esta caja fue desactivada por el administrador.");

            if (!request.Takeover)
            {
                var openSession = await _unitOfWork.CashRegisterSessions
                    .GetOpenSessionByRegisterAsync(existing.Id);

                throw new RegisterNameTakenException(
                    existingRegisterId: existing.Id,
                    hasOpenSession: openSession != null);
            }

            // Takeover branch: free the requested DeviceId from any other
            // register in this branch (permissive policy) before reassigning it,
            // so the unique partial index on DeviceId cannot fire.
            if (deviceId.HasValue)
            {
                var collidingRegister = await _unitOfWork.CashRegisters
                    .GetByDeviceIdAsync(branchId, deviceId.Value);

                if (collidingRegister != null && collidingRegister.Id != existing.Id)
                {
                    collidingRegister.DeviceId = null;
                    _unitOfWork.CashRegisters.Update(collidingRegister);
                }
            }

            existing.DeviceId = deviceId;
            _unitOfWork.CashRegisters.Update(existing);
            await _unitOfWork.SaveChangesAsync();

            // Reload with Device nav so the response carries the nested DTO.
            var refreshed = await _unitOfWork.CashRegisters.GetByIdWithDeviceAsync(existing.Id);
            return MapToDto(refreshed!);
        }

        var register = new CashRegister
        {
            BranchId = branchId,
            Name = request.Name,
            DeviceId = deviceId
        };

        await _unitOfWork.CashRegisters.AddAsync(register);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Name was free a moment ago (race) or the device is already
            // bound to another register and the caller did not opt into takeover.
            throw new ValidationException(
                "A cash register with that name or device already exists for this branch.");
        }

        var created = await _unitOfWork.CashRegisters.GetByIdWithDeviceAsync(register.Id);
        return MapToDto(created!);
    }

    /// <summary>
    /// Updates a cash register's name and/or bound device.
    /// </summary>
    public async Task<CashRegisterDto> UpdateRegisterAsync(int registerId, int branchId, UpdateCashRegisterRequest request)
    {
        // Normalize name to keep the unique (BranchId, Name) index case-insensitive.
        request.Name = request.Name.Trim().ToLowerInvariant();

        var register = await _unitOfWork.CashRegisters.GetByIdAsync(registerId)
            ?? throw new NotFoundException($"Cash register with id {registerId} not found");

        if (register.BranchId != branchId)
            throw new UnauthorizedException("Cash register does not belong to this branch");

        var deviceId = await ResolveDeviceIdAsync(request.DeviceUuid, branchId);

        register.Name = request.Name;
        register.DeviceId = deviceId;

        _unitOfWork.CashRegisters.Update(register);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException(
                "A cash register with that name or device already exists for this branch.");
        }

        var refreshed = await _unitOfWork.CashRegisters.GetByIdWithDeviceAsync(registerId);
        return MapToDto(refreshed!);
    }

    /// <summary>
    /// Toggles a cash register's active status.
    /// </summary>
    public async Task<CashRegisterDto> ToggleRegisterAsync(int registerId, int branchId)
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

        var refreshed = await _unitOfWork.CashRegisters.GetByIdWithDeviceAsync(registerId);
        return MapToDto(refreshed!);
    }

    /// <summary>
    /// Links a physical device to a cash register by its UUID.
    /// </summary>
    public async Task<CashRegisterDto> LinkDeviceAsync(int registerId, int branchId, LinkDeviceRequest request)
    {
        var register = await _unitOfWork.CashRegisters.GetByIdAsync(registerId)
            ?? throw new NotFoundException($"Cash register with id {registerId} not found");

        if (register.BranchId != branchId)
            throw new UnauthorizedException("Cash register does not belong to this branch");

        // Defensive engineering: the device must (a) exist and (b) belong to
        // the same branch as the register. A missing device or a cross-branch
        // device both surface as 404 instead of bubbling up as an FK violation.
        var device = await _unitOfWork.Devices.GetByDeviceUuidAndBranchAsync(request.DeviceUuid, branchId)
            ?? throw new NotFoundException("Device not found in this branch");

        register.DeviceId = device.Id;
        _unitOfWork.CashRegisters.Update(register);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException(
                "That device is already bound to another cash register in this branch.");
        }

        var refreshed = await _unitOfWork.CashRegisters.GetByIdWithDeviceAsync(registerId);
        return MapToDto(refreshed!);
    }

    /// <summary>
    /// Gets a cash register by its bound device UUID. Single-query JOIN —
    /// the repo eager-loads the Device navigation for the response DTO.
    /// </summary>
    public async Task<CashRegisterDto?> GetRegisterByDeviceUuidAsync(int branchId, string deviceUuid)
    {
        var register = await _unitOfWork.CashRegisters.GetByDeviceUuidAsync(branchId, deviceUuid);
        return register == null ? null : MapToDto(register);
    }

    #endregion

    #region Session Operations

    /// <summary>
    /// Gets the current open session for a specific register, or for the branch (legacy).
    /// If cashRegisterId is provided, fetches by register. Otherwise, fetches by branch.
    /// </summary>
    public async Task<CashRegisterSessionDto?> GetOpenSessionAsync(int branchId, int? cashRegisterId = null)
    {
        var session = cashRegisterId.HasValue
            ? await _unitOfWork.CashRegisterSessions.GetOpenSessionByRegisterAsync(cashRegisterId.Value)
            : await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);

        return session == null ? null : MapToDto(session);
    }

    /// <summary>
    /// Opens a new cash register session, optionally tied to a specific register.
    /// If CashRegisterId is provided, validates the register exists, belongs to the branch, and is active.
    /// Protected by unique filtered index at DB level. The opener identity is taken
    /// from the JWT-resolved <paramref name="userId"/>, not from the request body.
    /// </summary>
    public async Task<CashRegisterSessionDto> OpenSessionAsync(int branchId, int userId, OpenSessionRequest request)
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
            OpenedByUserId = userId,
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

        // Reload with .Include(OpenedByUser) so the response carries the nested DTO.
        var refreshed = request.CashRegisterId.HasValue
            ? await _unitOfWork.CashRegisterSessions.GetOpenSessionByRegisterAsync(request.CashRegisterId.Value)
            : await _unitOfWork.CashRegisterSessions.GetOpenSessionAsync(branchId);

        return MapToDto(refreshed!);
    }

    /// <summary>
    /// Closes the current open session with server-side financial calculations.
    /// Atomic: single transaction, concurrency-safe via xmin token. The closer
    /// identity is taken from the JWT-resolved <paramref name="userId"/>.
    /// </summary>
    public async Task<CashRegisterSessionDto> CloseSessionAsync(int branchId, int userId, CloseSessionRequest request, int? cashRegisterId = null)
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
        session.ClosedByUserId = userId;
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

        // After close the session moved to Closed status, so the open-session
        // lookups won't return it. Fetch by id with both user navs included
        // for the response DTO.
        var refreshed = await _unitOfWork.CashRegisterSessions.GetByIdWithUsersAsync(session.Id);
        return MapToDto(refreshed!);
    }

    /// <summary>
    /// Adds a cash movement to the open session of a specific register, or for
    /// the branch (legacy). Bumps the parent session's UpdatedAt to trigger
    /// xmin concurrency protection. The author identity is taken from the JWT.
    /// </summary>
    public async Task<CashMovementDto> AddMovementAsync(int branchId, int userId, AddMovementRequest request, int? cashRegisterId = null)
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
            CreatedByUserId = userId
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

        // Reload with the user nav populated so the DTO mapping has the data.
        var refreshed = await _unitOfWork.CashMovements.GetByIdWithUserAsync(movement.Id);
        return MapMovementToDto(refreshed!);
    }

    /// <summary>
    /// Gets cash register session history for a date range, projected to DTOs.
    /// </summary>
    public async Task<IEnumerable<CashRegisterSessionDto>> GetHistoryAsync(int branchId, DateOnly from, DateOnly to)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId)
            ?? throw new NotFoundException($"Branch with id {branchId} not found");

        // Align cash-register day boundaries with orders/reports (BDD-013):
        // compute startUtc from the `from` local midnight, endUtc from the
        // `to` local end-of-day so the range is half-open [startUtc, endUtc).
        var (startUtc, _) = TimeZoneHelper.GetUtcRangeForLocalDate(from, branch.TimeZoneId);
        var (_, endUtc) = TimeZoneHelper.GetUtcRangeForLocalDate(to, branch.TimeZoneId);

        var sessions = await _unitOfWork.CashRegisterSessions.GetHistoryAsync(branchId, startUtc, endUtc);
        return sessions.Select(MapToDto);
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

    /// <summary>
    /// Resolves an incoming <c>DeviceUuid</c> string from a request DTO into
    /// the strict <c>DeviceId</c> FK. Branch-scoped: cross-branch UUIDs are
    /// rejected as 404. Returns <c>null</c> when the input is null/empty so
    /// callers can keep the "unbound register" semantics intact.
    /// </summary>
    private async Task<int?> ResolveDeviceIdAsync(string? deviceUuid, int branchId)
    {
        if (string.IsNullOrWhiteSpace(deviceUuid))
            return null;

        var device = await _unitOfWork.Devices.GetByDeviceUuidAndBranchAsync(deviceUuid, branchId)
            ?? throw new NotFoundException("Device not found in this branch");

        return device.Id;
    }

    private static CashRegisterDto MapToDto(CashRegister register)
    {
        return new CashRegisterDto
        {
            Id = register.Id,
            BranchId = register.BranchId,
            Name = register.Name,
            DeviceId = register.DeviceId,
            IsActive = register.IsActive,
            CreatedAt = register.CreatedAt,
            Device = register.Device == null ? null : new DeviceDto
            {
                Id = register.Device.Id,
                DeviceUuid = register.Device.DeviceUuid,
                Name = register.Device.Name,
                Mode = register.Device.Mode,
                IsActive = register.Device.IsActive
            }
        };
    }

    private static UserSummaryDto? MapUser(User? user)
    {
        return user == null ? null : new UserSummaryDto
        {
            Id = user.Id,
            Name = user.Name,
            RoleId = user.RoleId
        };
    }

    private static CashRegisterSessionDto MapToDto(CashRegisterSession session)
    {
        return new CashRegisterSessionDto
        {
            Id = session.Id,
            BranchId = session.BranchId,
            CashRegisterId = session.CashRegisterId,
            OpenedByUserId = session.OpenedByUserId,
            OpenedByUser = MapUser(session.OpenedByUser),
            OpenedAt = session.OpenedAt,
            InitialAmountCents = session.InitialAmountCents,
            ClosedByUserId = session.ClosedByUserId,
            ClosedByUser = MapUser(session.ClosedByUser),
            ClosedAt = session.ClosedAt,
            CountedAmountCents = session.CountedAmountCents,
            CashSalesCents = session.CashSalesCents,
            TotalCashInCents = session.TotalCashInCents,
            TotalCashOutCents = session.TotalCashOutCents,
            ExpectedAmountCents = session.ExpectedAmountCents,
            DifferenceCents = session.DifferenceCents,
            Notes = session.Notes,
            CashRegisterStatusId = session.CashRegisterStatusId,
            UpdatedAt = session.UpdatedAt
        };
    }

    private static CashMovementDto MapMovementToDto(CashMovement movement)
    {
        return new CashMovementDto
        {
            Id = movement.Id,
            SessionId = movement.SessionId,
            CashMovementTypeId = movement.CashMovementTypeId,
            AmountCents = movement.AmountCents,
            Description = movement.Description,
            CreatedByUserId = movement.CreatedByUserId,
            CreatedByUser = MapUser(movement.CreatedByUser),
            CreatedAt = movement.CreatedAt
        };
    }

    #endregion
}
