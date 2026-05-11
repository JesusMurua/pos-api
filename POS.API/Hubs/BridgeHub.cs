using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using POS.Domain.Enums;
using POS.Services.IService;

namespace POS.API.Hubs;

/// <summary>
/// SignalR hub used by the local hardware bridge (Windows Service controlling
/// turnstiles, biometric readers, thermal printers) to receive real-time
/// commands from the backend (e.g. "OpenTurnstile" for a granted access).
/// One bridge connection per branch — the group name is keyed only by
/// <c>branchId</c>, no destination sub-channel.
/// </summary>
[Authorize]
public class BridgeHub : Hub
{
    /// <summary>
    /// Builds the SignalR group name shared between the hub (on connect) and
    /// the <see cref="IBridgeNotifier"/> implementation (on broadcast).
    /// Centralising prevents drift between sender and receiver.
    /// </summary>
    public static string BuildGroupName(int branchId)
        => $"bridge-branch-{branchId}";

    public override async Task OnConnectedAsync()
    {
        // Accepts both human user JWTs (manager opening the bridge dashboard)
        // and long-lived device JWTs (type=device, mode=bridge). Only the
        // tenant-scoped claims are required.
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

        await Groups.AddToGroupAsync(Context.ConnectionId, BuildGroupName(branchId));
        await base.OnConnectedAsync();
    }
}
