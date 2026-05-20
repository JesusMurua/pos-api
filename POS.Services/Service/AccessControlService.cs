using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Domain.DTOs.AccessControl;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements <see cref="IAccessControlService"/>. Coordinates the HMAC lookup,
/// membership evaluation, audit logging, and SignalR push. Frozen and Cancelled
/// memberships now map to their dedicated <c>AccessReasonIds.MembershipFrozen</c>
/// and <c>MembershipCancelled</c> codes (Phase 3).
/// </summary>
public class AccessControlService : IAccessControlService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHmacService _hmacService;
    private readonly IBridgeNotifier _bridgeNotifier;
    private readonly IFeatureGateService _featureGate;
    private readonly ILogger<AccessControlService> _logger;

    public AccessControlService(
        IUnitOfWork unitOfWork,
        IHmacService hmacService,
        IBridgeNotifier bridgeNotifier,
        IFeatureGateService featureGate,
        ILogger<AccessControlService> logger)
    {
        _unitOfWork = unitOfWork;
        _hmacService = hmacService;
        _bridgeNotifier = bridgeNotifier;
        _featureGate = featureGate;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates access for a scanned QR token. Frozen and Cancelled memberships
    /// map to their dedicated <c>AccessReasonIds.MembershipFrozen</c> and
    /// <c>MembershipCancelled</c> codes (Phase 3 enhancement).
    ///
    /// DEFERRED DEBT (M4): Mid-period cancellations with future ValidUntil may be
    /// prioritized over recent Expired rows by GetLatestForCustomerAsync. Requires
    /// MembershipService to update UpdatedAt on state transitions.
    /// </summary>
    public async Task<AccessResultDto> EvaluateQrAccessAsync(
        string plainQrToken, int callerBranchId, int callerBusinessId)
    {
        if (!await _featureGate.IsEnabledAsync(callerBusinessId, FeatureKey.RealtimeAccessControl))
        {
            var business = await _unitOfWork.Business.GetByIdAsync(callerBusinessId);
            throw new PlanLimitExceededException(
                resource: "RealtimeAccessControl",
                limit: 0,
                currentPlan: PlanTypeIds.ToCode(business!.PlanTypeId));
        }

        // (a) Hash incoming token with the server-side HMAC secret. Lookup is
        // O(log n) thanks to the unique partial index IX_Customers_BusinessId_QrToken.
        var hashedToken = _hmacService.ComputeHash(plainQrToken);

        // (b) Tenant-scoped customer lookup. Unique partial index guarantees ≤1.
        var customer = (await _unitOfWork.Customers.GetAsync(c =>
                c.BusinessId == callerBusinessId && c.QrToken == hashedToken))
            .FirstOrDefault();

        if (customer is null)
        {
            // Unknown QR — no AccessLog row written (FK to Customers is required).
            // Forensic trail lives in the structured log instead. The dashboard
            // still receives the broadcast so admins see unidentified scans in
            // real time alongside identified ones.
            _logger.LogWarning(
                "Unknown QR token scanned at branch {BranchId} (business {BusinessId})",
                callerBranchId, callerBusinessId);

            var unknownDto = new AccessResultDto
            {
                IsGranted = false,
                AccessReasonId = AccessReasonIds.NoMembership,
                CustomerId = null
            };
            await _bridgeNotifier.NotifyAccessAttemptAsync(callerBranchId, unknownDto);
            return unknownDto;
        }

        // (c) Format display name once for reuse in the result DTO.
        var fullName = customer.LastName != null
            ? $"{customer.FirstName} {customer.LastName}"
            : customer.FirstName;

        // TODO (Phase 4: Billing Engine) — evaluation of payment_overdue against CreditBalance/Invoices.

        // (e) Look up the first currently-active membership. The IsCurrentlyActive
        // extension cannot be translated to SQL, so the predicate is inlined at
        // the database boundary.
        var now = DateTime.UtcNow;
        var activeMembership = (await _unitOfWork.CustomerMemberships.GetAsync(m =>
                m.CustomerId == customer.Id
                && m.Status == MembershipStatus.Active
                && m.ValidUntil >= now))
            .FirstOrDefault();

        // (f) Decide reason + populate the response DTO.
        int reason;
        bool granted;
        int? membershipId;

        if (activeMembership is not null)
        {
            reason = AccessReasonIds.MembershipActive;
            granted = true;
            membershipId = activeMembership.Id;
        }
        else
        {
            // Fetch the most recent membership row (entity, not DTO) so we can
            // switch on the raw Status enum and surface the precise denial
            // reason — Frozen and Cancelled get their own audit codes instead
            // of being lumped into the generic "Expired" bucket.
            var latestMembership = await _unitOfWork.CustomerMemberships
                .GetLatestForCustomerAsync(customer.Id);

            reason = latestMembership switch
            {
                null => AccessReasonIds.NoMembership,
                { Status: MembershipStatus.Frozen } => AccessReasonIds.MembershipFrozen,
                { Status: MembershipStatus.Cancelled } => AccessReasonIds.MembershipCancelled,
                _ => AccessReasonIds.MembershipExpired
            };
            granted = false;
            membershipId = null;
        }

        // (g) Build the response DTO before persistence so both the dashboard
        // broadcast and the HTTP return value reference the exact same payload.
        // AccessLogId is populated after SaveChangesAsync — see step (i).
        var resultDto = new AccessResultDto
        {
            IsGranted = granted,
            AccessReasonId = reason,
            CustomerId = customer.Id,
            CustomerName = fullName,
            CustomerMembershipId = membershipId
        };

        // (h) Write the audit row. BranchId is overwritten by
        // BranchInjectionInterceptor from the JWT, but the explicit assignment
        // is defensive in case the interceptor is ever removed.
        var accessLog = new AccessLog
        {
            BranchId = callerBranchId,
            CustomerId = customer.Id,
            CustomerMembershipId = membershipId,
            AccessReasonId = reason,
            AccessMethodId = AccessMethodIds.Qr,
            IsGranted = granted,
            OccurredAt = now
        };
        await _unitOfWork.AccessLogs.AddAsync(accessLog);
        await _unitOfWork.SaveChangesAsync();

        // (i) Propagate the DB-generated id so dashboard clients can deep-link
        // from the live feed into the persisted audit row.
        resultDto.AccessLogId = accessLog.Id;

        // (j) Real-time push. Awaited so SignalR exceptions surface to the
        // caller / middleware instead of being swallowed as unobserved tasks.
        // Hardware command fires only on granted access; the dashboard feed
        // fires on every persisted attempt.
        if (granted)
        {
            await _bridgeNotifier.NotifyAccessGrantedAsync(callerBranchId, customer.Id);
        }
        await _bridgeNotifier.NotifyAccessAttemptAsync(callerBranchId, resultDto);

        return resultDto;
    }

    /// <inheritdoc />
    public async Task EnrollQrTokenAsync(int customerId, string plainQrToken, int callerBusinessId)
    {
        // Tenant-scoped fetch. Cross-tenant ids resolve to null and surface as
        // CUSTOMER_NOT_FOUND so the response never confirms whether the id
        // exists in some other business.
        var customer = (await _unitOfWork.Customers.GetAsync(c =>
                c.Id == customerId && c.BusinessId == callerBusinessId))
            .FirstOrDefault();

        if (customer is null)
            throw new ValidationException("CUSTOMER_NOT_FOUND");

        customer.QrToken = _hmacService.ComputeHash(plainQrToken);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique partial index IX_Customers_BusinessId_QrToken violated:
            // some other customer in this business already has this hash.
            throw new ValidationException("QR_TOKEN_ALREADY_ASSIGNED");
        }
    }

    /// <inheritdoc />
    public async Task<QrStatusResponseDto> GetCustomerQrStatusAsync(int customerId, int callerBusinessId)
    {
        var customer = (await _unitOfWork.Customers.GetAsync(c =>
                c.Id == customerId && c.BusinessId == callerBusinessId))
            .FirstOrDefault();

        if (customer is null)
            throw new ValidationException("CUSTOMER_NOT_FOUND");

        return new QrStatusResponseDto
        {
            HasEnrolledQr = !string.IsNullOrEmpty(customer.QrToken)
        };
    }

    /// <inheritdoc />
    public async Task RevokeQrTokenAsync(int customerId, int callerBusinessId)
    {
        var customer = (await _unitOfWork.Customers.GetAsync(c =>
                c.Id == customerId && c.BusinessId == callerBusinessId))
            .FirstOrDefault();

        if (customer is null)
            throw new ValidationException("CUSTOMER_NOT_FOUND");

        customer.QrToken = null;
        await _unitOfWork.SaveChangesAsync();

        // Log AFTER successful commit so the forensic trail never claims a
        // revocation that didn't actually persist (e.g. on SaveChanges failure).
        _logger.LogInformation(
            "QR token revoked for Customer {CustomerId} by Business {BusinessId}",
            customerId, callerBusinessId);
    }
}
