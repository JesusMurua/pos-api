using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using POS.Domain.DTOs.Bridge;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.API.Hubs;

/// <summary>
/// SignalR hub used by the local Fino Bridge (Windows Service controlling
/// turnstiles, biometric readers, thermal printers, scales) and by the
/// reception dashboard. Audiences are segregated by group:
/// <list type="bullet">
///   <item><c>bridge-hardware-{branchId}</c> — devices with <c>mode=bridge</c>.
///         Receive hardware commands like <c>OpenTurnstile</c>.</item>
///   <item><c>bridge-dashboard-{branchId}</c> — every other connected client
///         (reception terminals, admin dashboards). Receive telemetry like
///         <c>AccessAttempted</c> and <c>OnWeightUpdated</c>.</item>
/// </list>
/// </summary>
[Authorize]
public class BridgeHub : Hub
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BridgeHub> _logger;

    public BridgeHub(IUnitOfWork unitOfWork, ILogger<BridgeHub> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>Group joined by bridge-mode devices to receive hardware commands.</summary>
    public static string BuildHardwareGroupName(int branchId)
        => $"bridge-hardware-{branchId}";

    /// <summary>Group joined by non-bridge clients (dashboards) to receive telemetry.</summary>
    public static string BuildDashboardGroupName(int branchId)
        => $"bridge-dashboard-{branchId}";

    /// <summary>
    /// Denial reasons the bridge is permitted to emit via <c>ScanPayloadDto.DenialReasonId</c>.
    /// Granted scans always force <see cref="AccessReasonIds.MembershipActive"/>; bogus
    /// or out-of-range denial codes fall back to <see cref="AccessReasonIds.NoMembership"/>
    /// to avoid FK violations against the AccessReasonCatalogs table.
    /// </summary>
    private static readonly HashSet<int> ValidDenialReasons = new()
    {
        AccessReasonIds.PaymentOverdue,
        AccessReasonIds.MembershipExpired,
        AccessReasonIds.NoMembership,
        AccessReasonIds.ManualOverride,
        AccessReasonIds.MembershipFrozen,
        AccessReasonIds.MembershipCancelled
    };

    private int GetBranchId() => int.Parse(Context.User!.FindFirst("branchId")!.Value);

    private int GetBusinessId() => int.Parse(Context.User!.FindFirst("businessId")!.Value);

    private int GetDeviceId() => int.Parse(Context.User!.FindFirst("deviceId")!.Value);

    private bool IsBridgeMode() => Context.User?.FindFirst("mode")?.Value == DeviceModeCodes.Bridge;

    private void EnsureBridgeMode()
    {
        if (!IsBridgeMode())
            throw new HubException("This hub method requires a bridge-mode device token.");
    }

    public override async Task OnConnectedAsync()
    {
        // Accepts both human user JWTs (manager opening the reception dashboard)
        // and long-lived device JWTs (type=device, mode=bridge). Only the
        // tenant-scoped claims are required to land in a group.
        var branchClaim = Context.User?.FindFirst("branchId")?.Value;
        if (!int.TryParse(branchClaim, out var branchId))
        {
            Context.Abort();
            return;
        }

        var businessClaim = Context.User?.FindFirst("businessId")?.Value;
        if (!int.TryParse(businessClaim, out var businessId))
        {
            Context.Abort();
            return;
        }

        // Plan × giro gate. Only businesses with RealtimeAccessControl enabled
        // can keep a live bridge connection — without this, a downgraded plan
        // would still receive turnstile commands for free.
        var httpContext = Context.GetHttpContext();
        var featureGate = httpContext?.RequestServices.GetRequiredService<IFeatureGateService>();
        if (featureGate == null || !await featureGate.IsEnabledAsync(businessId, FeatureKey.RealtimeAccessControl))
        {
            Context.Abort();
            return;
        }

        // Segregated grouping: bridges receive hardware commands, everyone else
        // (reception terminals, admin dashboards) receives telemetry.
        var groupName = IsBridgeMode()
            ? BuildHardwareGroupName(branchId)
            : BuildDashboardGroupName(branchId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        await base.OnConnectedAsync();

        // Push the offline access cache only to bridges. Dashboards do not need
        // the 5k-row sync payload and would just discard it.
        if (IsBridgeMode())
        {
            try
            {
                await PushSyncDataToCaller();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Initial sync push failed for branch {BranchId}", branchId);
            }
        }
    }

    /// <summary>
    /// Records a hardware scan from the bridge as an <see cref="AccessLog"/>
    /// row. The bridge is the local authority on grant/deny; the cloud only
    /// validates tenant ownership, time-bounds, and FK integrity, then writes.
    /// </summary>
    public async Task ProcessScan(ScanPayloadDto payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EnsureBridgeMode();

        if (!int.TryParse(payload.Identifier, out var customerId))
        {
            _logger.LogWarning(
                "Bridge sent non-numeric Identifier {Identifier} from branch {BranchId}",
                payload.Identifier, GetBranchId());
            return;
        }

        // Tenant guard — bridges must never insert AccessLog rows pointing at
        // customers of a sibling business. AnyAsync uses the (BusinessId, Id)
        // covering index and avoids materialising the entity.
        var businessId = GetBusinessId();
        var customerInTenant = await _unitOfWork.Customers.AnyAsync(c =>
            c.Id == customerId && c.BusinessId == businessId);
        if (!customerInTenant)
        {
            _logger.LogWarning(
                "Bridge scan refers to unknown or cross-tenant CustomerId {CustomerId} (business {BusinessId})",
                customerId, businessId);
            return;
        }

        // Trust-but-clamp the bridge clock. Accept replay window of 30 days for
        // offline cache catch-up; clamp future drift to the server's UtcNow.
        var now = DateTime.UtcNow;
        var occurredAt = payload.ScanUtc;
        if (occurredAt > now.AddMinutes(5))
            occurredAt = now;
        if (occurredAt < now.AddDays(-30))
        {
            _logger.LogWarning(
                "Bridge scan dropped — stale ScanUtc {ScanUtc} for CustomerId {CustomerId}",
                payload.ScanUtc, customerId);
            return;
        }

        var methodId = payload.Source?.ToLowerInvariant() switch
        {
            "biometric" => AccessMethodIds.Biometric,
            "qr"        => AccessMethodIds.Qr,
            _           => AccessMethodIds.Manual
        };

        // Granted scans always log MembershipActive. Denied scans honour the
        // bridge's specific reason when it lands in the catalog whitelist;
        // otherwise collapse to NoMembership so the FK constraint never fires.
        int reasonId = payload.Authorized
            ? AccessReasonIds.MembershipActive
            : (payload.DenialReasonId is int r && ValidDenialReasons.Contains(r)
                ? r
                : AccessReasonIds.NoMembership);

        // Link the audit row to the membership that authorised the access so
        // downstream reporting can answer "how many entries did membership #N
        // grant?". Skip the lookup for denials — there is no authorising row.
        int? membershipId = null;
        if (payload.Authorized)
        {
            var active = (await _unitOfWork.CustomerMemberships.GetAsync(m =>
                    m.CustomerId == customerId
                    && m.Status == MembershipStatus.Active
                    && m.ValidUntil >= now))
                .FirstOrDefault();
            membershipId = active?.Id;
        }

        var accessLog = new AccessLog
        {
            BranchId = GetBranchId(),
            CustomerId = customerId,
            CustomerMembershipId = membershipId,
            AccessReasonId = reasonId,
            AccessMethodId = methodId,
            IsGranted = payload.Authorized,
            OccurredAt = occurredAt
        };

        await _unitOfWork.AccessLogs.AddAsync(accessLog);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Bumps <see cref="Device.LastSeenAt"/> for the calling bridge device and
    /// emits a structured health log including the rest of the telemetry.
    /// </summary>
    public async Task ReportHealth(HealthStatusDto status)
    {
        ArgumentNullException.ThrowIfNull(status);
        EnsureBridgeMode();

        var deviceId = GetDeviceId();
        var device = await _unitOfWork.Devices.GetByIdAsync(deviceId);
        if (device is not null)
        {
            // EF tracks the entity from GetByIdAsync; assigning the property and
            // SaveChangesAsync is enough — no need for explicit Update() call.
            device.LastSeenAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
        }

        _logger.LogInformation(
            "Bridge health [Branch {BranchId}, Device {DeviceId}] Connected:{Connected} Devices:{DeviceCount} LastSync:{LastSync}",
            GetBranchId(), deviceId, status.IsCloudConnected, status.ConfiguredDevicesCount, status.LastSyncUtc);
    }

    /// <summary>
    /// Forwards a live scale reading to the dashboard group only. The bridge
    /// itself does not receive its own broadcast back (no echo).
    /// </summary>
    public async Task ProcessWeightRead(WeightPayloadDto payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        EnsureBridgeMode();

        await Clients
            .Group(BuildDashboardGroupName(GetBranchId()))
            .SendAsync("OnWeightUpdated", payload);
    }

    /// <summary>
    /// Pushes the offline access cache (active memberships + QR hashes) to the
    /// caller. Used on bridge connection so the bridge can authorise scans
    /// locally without round-tripping the cloud.
    /// </summary>
    private async Task PushSyncDataToCaller()
    {
        var businessId = GetBusinessId();
        var now = DateTime.UtcNow;

        // includeProperties eager-loads Customer so the projection below can
        // pull QrToken + name without an N+1 round-trip per row.
        var memberships = await _unitOfWork.CustomerMemberships.GetAsync(
            m => m.Status == MembershipStatus.Active
                 && m.ValidUntil >= now
                 && m.Customer!.BusinessId == businessId,
            includeProperties: "Customer");

        var records = memberships
            .Select(m => new SyncAccessRecordDto
            {
                CustomerId = m.CustomerId,
                QrTokenHash = m.Customer!.QrToken,
                ExpirationUtc = m.ValidUntil,
                CustomerName = m.Customer!.LastName != null
                    ? $"{m.Customer.FirstName} {m.Customer.LastName}"
                    : m.Customer.FirstName
            })
            .ToList();

        await Clients.Caller.SendAsync("SyncAccessData", records);
    }
}
