using POS.Domain.DTOs.Pos;

namespace POS.Services.IService;

/// <summary>
/// Single entry point for the POS first-time-setup orchestration. The
/// implementation runs device-register + cash-register-create-or-takeover
/// + link inside one atomic transaction so a partial failure cannot leave
/// an orphan register or a register pointing at a stale device id (the
/// failure mode that produced the prod orphan id=18 reported in
/// AUDIT-POS-first-time-entry).
/// </summary>
public interface ICashierSessionService
{
    /// <summary>
    /// Materializes (or recovers) the cashier's register binding for the
    /// caller's browser. See the
    /// <see cref="InitializeCashierSessionRequest"/> and
    /// <see cref="InitializeOutcome"/> docs for the exact branching.
    /// </summary>
    /// <param name="businessId">From the JWT <c>businessId</c> claim.</param>
    /// <param name="claimBranchId">From the JWT <c>branchId</c> claim — used when the request does not override.</param>
    /// <param name="userId">Calling user, used as <c>ClosedByUserId</c> for force-takeover.</param>
    /// <param name="userRoleId">Calling user's role; bypasses UserBranches check when admin.</param>
    /// <param name="request">The request body.</param>
    Task<InitializeCashierSessionResponse> InitializeAsync(
        int businessId,
        int claimBranchId,
        int userId,
        int userRoleId,
        InitializeCashierSessionRequest request);
}

/// <summary>
/// Sentinel exception the orchestrator throws when a register already has
/// an open session on a different device and the caller did not opt into
/// the force-takeover flow. The controller catches this and returns a
/// 409 with the existing-register + open-session ids so the SPA can
/// prompt the operator.
/// </summary>
public sealed class SessionOpenOnOtherDeviceException : Exception
{
    public int ExistingRegisterId { get; }
    public int OpenSessionId { get; }
    public string RegisterName { get; }

    public SessionOpenOnOtherDeviceException(int existingRegisterId, int openSessionId, string registerName)
        : base($"Register {registerName} has an open session ({openSessionId}) on another device.")
    {
        ExistingRegisterId = existingRegisterId;
        OpenSessionId = openSessionId;
        RegisterName = registerName;
    }
}
