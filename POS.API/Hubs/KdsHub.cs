using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using POS.Domain.Enums;
using POS.Services.IService;

namespace POS.API.Hubs;

/// <summary>
/// SignalR hub used by Kitchen Display Systems to receive real-time
/// notifications when new print jobs are generated for their branch/destination.
/// Clients connect with a JWT in the <c>access_token</c> query string and
/// specify the KDS area they want to subscribe to via the <c>destination</c>
/// query string (e.g. <c>?destination=Kitchen</c>).
/// </summary>
[Authorize]
public class KdsHub : Hub
{
    /// <summary>
    /// Group name prefix used for broadcasts. A connection is placed in the
    /// group <c>branch-{branchId}-{destination}</c>. The dispatcher worker
    /// sends events to the same group name so only the intended KDS station
    /// receives the payload.
    /// </summary>
    public const string GroupPrefix = "branch-";

    /// <summary>
    /// Builds the SignalR group name used by both the hub (on connect) and the
    /// dispatcher worker (on broadcast). Centralising this avoids drift.
    /// </summary>
    public static string BuildGroupName(int branchId, string destination)
        => $"{GroupPrefix}{branchId}-{destination}";

    public override async Task OnConnectedAsync()
    {
        // Accepts both human user JWTs and long-lived device JWTs (type=device).
        // Only tenant-scoped claims are required; NameIdentifier/roleId are intentionally
        // not inspected so KDS/kiosk hardware can authenticate without a human session.
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

        var httpContext = Context.GetHttpContext();
        var destination = httpContext?.Request.Query["destination"].ToString();
        if (string.IsNullOrWhiteSpace(destination))
        {
            Context.Abort();
            return;
        }

        // Realtime KDS is gated by plan × giro. Polling-based PrintJobController
        // stays accessible; only the socket push layer is restricted here.
        var featureGate = httpContext?.RequestServices.GetRequiredService<IFeatureGateService>();
        if (featureGate == null || !await featureGate.IsEnabledAsync(businessId, FeatureKey.RealtimeKds))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BuildGroupName(branchId, destination));
        await base.OnConnectedAsync();
    }
}
