using Microsoft.AspNetCore.SignalR;
using POS.API.Hubs;
using POS.Domain.DTOs.AccessControl;
using POS.Domain.DTOs.Bridge;
using POS.Services.IService;

namespace POS.API.Adapter;

/// <summary>
/// SignalR-backed implementation of <see cref="IBridgeNotifier"/>. Lives in
/// <c>POS.API</c> because <c>BridgeHub</c> and <c>IHubContext&lt;BridgeHub&gt;</c>
/// are only resolvable inside the web host — keeps <c>POS.Services</c> free of
/// any reference to <c>POS.API</c>.
/// </summary>
public class BridgeNotifier : IBridgeNotifier
{
    private readonly IHubContext<BridgeHub> _hub;

    public BridgeNotifier(IHubContext<BridgeHub> hub)
    {
        _hub = hub;
    }

    /// <inheritdoc />
    public Task NotifyAccessGrantedAsync(int branchId, int customerId)
        => _hub.Clients
            .Group(BridgeHub.BuildHardwareGroupName(branchId))
            .SendAsync("OpenTurnstile", customerId);

    /// <inheritdoc />
    public Task NotifyAccessAttemptAsync(int branchId, AccessResultDto result)
        => _hub.Clients
            .Group(BridgeHub.BuildDashboardGroupName(branchId))
            .SendAsync("AccessAttempted", result);

    /// <inheritdoc />
    public Task SendEscPosCommandAsync(int branchId, string printerId, byte[] escPosBytes)
    {
        ArgumentNullException.ThrowIfNull(escPosBytes);
        var base64 = Convert.ToBase64String(escPosBytes);
        var payload = new EscPosPayloadDto(printerId, base64);

        // FIRE-AND-FORGET WARNING: Without a persistent Outbox, this message will be lost if the bridge is disconnected.
        // A full PrintJobOutbox migration is required for guaranteed delivery in future phases.
        return _hub.Clients
            .Group(BridgeHub.BuildHardwareGroupName(branchId))
            .SendAsync("SendEscPosCommand", payload);
    }
}
