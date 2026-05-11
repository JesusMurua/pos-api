using Microsoft.AspNetCore.SignalR;
using POS.API.Hubs;
using POS.Domain.DTOs.AccessControl;
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
            .Group(BridgeHub.BuildGroupName(branchId))
            .SendAsync("OpenTurnstile", customerId);

    /// <inheritdoc />
    public Task NotifyAccessAttemptAsync(int branchId, AccessResultDto result)
        => _hub.Clients
            .Group(BridgeHub.BuildGroupName(branchId))
            .SendAsync("AccessAttempted", result);
}
