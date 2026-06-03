using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Domain.DTOs.Pos;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc cref="ICashierSessionService"/>
public class CashierSessionService : ICashierSessionService
{
    private const string DefaultRegisterName = "Caja Principal";
    private const string DefaultDeviceName = "Caja Web";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDeviceService _deviceService;
    private readonly ICashRegisterService _cashRegisterService;
    private readonly ILogger<CashierSessionService> _logger;

    public CashierSessionService(
        IUnitOfWork unitOfWork,
        IDeviceService deviceService,
        ICashRegisterService cashRegisterService,
        ILogger<CashierSessionService> logger)
    {
        _unitOfWork = unitOfWork;
        _deviceService = deviceService;
        _cashRegisterService = cashRegisterService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<InitializeCashierSessionResponse> InitializeAsync(
        int businessId,
        int claimBranchId,
        int userId,
        int userRoleId,
        InitializeCashierSessionRequest request)
    {
        var branchId = request.BranchIdOverride ?? claimBranchId;
        var normalizedName = (request.RegisterName ?? DefaultRegisterName)
            .Trim()
            .ToLowerInvariant();
        var deviceName = request.DeviceName ?? DefaultDeviceName;

        await ValidateBranchAccessAsync(businessId, branchId, userId, userRoleId);

        // Single transaction wraps device upsert + register create-or-takeover
        // + optional force-close. await using guarantees rollback on any
        // unhandled exception thrown by the orchestrator helpers.
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        var device = await _deviceService.EnsureRegisteredAsync(
            request.DeviceUuid, branchId, DeviceModeCodes.Cashier, deviceName);

        // Flush device id assignment so the register step can reference it.
        await _unitOfWork.SaveChangesAsync();

        var existing = await _unitOfWork.CashRegisters.GetByNameAsync(branchId, normalizedName);

        CashRegister register;
        InitializeOutcome outcome;
        int? closedSessionId = null;

        if (existing is null)
        {
            register = new CashRegister
            {
                BranchId = branchId,
                Name = normalizedName,
                DeviceId = device.Id
            };
            await _unitOfWork.CashRegisters.AddAsync(register);

            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Concurrent INSERT race on the unique (BranchId, Name)
                // index — fall through to the "existing" branch with a
                // fresh lookup. One retry only; if it still collides we
                // surface a clean error.
                existing = await _unitOfWork.CashRegisters.GetByNameAsync(branchId, normalizedName)
                    ?? throw new ValidationException(
                        "Could not create the register due to a concurrent creation conflict.");
                register = existing;
                outcome = InitializeOutcome.Idempotent; // fall through into reconcile
            }

            outcome = InitializeOutcome.Created;
        }
        else
        {
            register = existing;
            outcome = await ReconcileExistingRegisterAsync(
                register, device.Id, userId, request.Force);

            if (outcome == InitializeOutcome.ForceTakeover)
            {
                var openSession = await _unitOfWork.CashRegisterSessions
                    .GetOpenSessionByRegisterAsync(register.Id);
                closedSessionId = openSession?.Id;
            }

            await _unitOfWork.SaveChangesAsync();
        }

        await transaction.CommitAsync();

        // Refetch with Device nav so the response carries a consistent
        // projection without depending on tracked-entity state.
        var refreshed = await _unitOfWork.CashRegisters.GetByIdWithDeviceAsync(register.Id)
            ?? register;

        _logger.LogInformation(
            "InitializeCashierSession {@InitializeCashierSessionAudit}",
            new
            {
                Timestamp = DateTime.UtcNow,
                BusinessId = businessId,
                BranchId = branchId,
                UserId = userId,
                DeviceUuid = request.DeviceUuid,
                DeviceId = device.Id,
                RegisterId = register.Id,
                Outcome = outcome.ToString(),
                ClosedSessionId = closedSessionId,
                ForceFlag = request.Force
            });

        return new InitializeCashierSessionResponse(
            Device: new DeviceInfo(
                Id: device.Id,
                Uuid: device.DeviceUuid,
                Mode: device.Mode,
                Name: device.Name,
                BranchId: device.BranchId),
            Register: new CashRegisterInfo(
                Id: refreshed.Id,
                Name: refreshed.Name,
                DeviceId: refreshed.DeviceId,
                IsActive: refreshed.IsActive),
            Outcome: outcome,
            ClosedSessionId: closedSessionId);
    }

    /// <summary>
    /// Decides what to do when the register already exists. Mutates
    /// <paramref name="register"/> in place (tracked entity); the outer
    /// orchestrator calls <c>SaveChangesAsync</c> + <c>CommitAsync</c>.
    /// Throws <see cref="SessionOpenOnOtherDeviceException"/> when the
    /// caller did not opt into force-takeover but the register has an
    /// open session on a different device.
    /// </summary>
    private async Task<InitializeOutcome> ReconcileExistingRegisterAsync(
        CashRegister register, int newDeviceId, int userId, bool force)
    {
        if (!register.IsActive)
            throw new ValidationException("Esta caja fue desactivada por el administrador.");

        if (register.DeviceId == newDeviceId)
            return InitializeOutcome.Idempotent;

        if (register.DeviceId is null)
        {
            register.DeviceId = newDeviceId;
            return InitializeOutcome.LinkedOrphan;
        }

        // Register points at a different device. Check for an open session
        // before deciding silent reassign vs prompt vs force-close.
        var openSession = await _unitOfWork.CashRegisterSessions
            .GetOpenSessionByRegisterAsync(register.Id);

        if (openSession is null)
        {
            await FreeDeviceFromOtherRegistersAsync(register.BranchId, newDeviceId, exceptRegisterId: register.Id);
            register.DeviceId = newDeviceId;
            return InitializeOutcome.Reassigned;
        }

        if (!force)
        {
            throw new SessionOpenOnOtherDeviceException(
                existingRegisterId: register.Id,
                openSessionId: openSession.Id,
                registerName: register.Name);
        }

        await FreeDeviceFromOtherRegistersAsync(register.BranchId, newDeviceId, exceptRegisterId: register.Id);
        await _cashRegisterService.ForceCloseSessionAsync(
            openSession.Id, userId, reason: "initialize_force_takeover");
        register.DeviceId = newDeviceId;
        return InitializeOutcome.ForceTakeover;
    }

    /// <summary>
    /// Releases <paramref name="deviceId"/> from any other register in the
    /// branch so the unique partial index on <c>(BranchId, DeviceId)</c>
    /// cannot fire when we assign the device to the target register. Same
    /// pattern as <c>CashRegisterService.CreateRegisterAsync:73-83</c>.
    /// </summary>
    private async Task FreeDeviceFromOtherRegistersAsync(int branchId, int deviceId, int exceptRegisterId)
    {
        var colliding = await _unitOfWork.CashRegisters.GetByDeviceIdAsync(branchId, deviceId);
        if (colliding != null && colliding.Id != exceptRegisterId)
        {
            colliding.DeviceId = null;
            _unitOfWork.CashRegisters.Update(colliding);
        }
    }

    /// <summary>
    /// Enforces the branch access rules for the calling user:
    /// <list type="bullet">
    ///   <item><description>Branch must exist (loaded via
    ///   <c>IgnoreQueryFilters</c> so an override can target any branch
    ///   the user controls).</description></item>
    ///   <item><description>Branch must belong to the JWT
    ///   <c>businessId</c> — cross-tenant returns <c>404</c> (same as
    ///   "not found") so the caller cannot probe branch existence.</description></item>
    ///   <item><description>Non-admin roles (Manager) need a
    ///   <c>UserBranches</c> assignment to the branch; Owner/admin
    ///   bypass.</description></item>
    /// </list>
    /// </summary>
    private async Task ValidateBranchAccessAsync(int businessId, int branchId, int userId, int userRoleId)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);
        if (branch is null || branch.BusinessId != businessId)
            throw new NotFoundException($"Branch with id {branchId} not found");

        if (UserRoleIds.IsAdminRole(userRoleId))
            return;

        var userBranches = await _unitOfWork.UserBranches.GetByUserIdAsync(userId);
        if (!userBranches.Any(ub => ub.BranchId == branchId))
            throw new UnauthorizedException("User is not assigned to the requested branch.");
    }
}
