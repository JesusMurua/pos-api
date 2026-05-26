using Microsoft.AspNetCore.SignalR;
using POS.API.Hubs;
using POS.Domain.DTOs.AccessControl;
using POS.Domain.DTOs.Bridge;
using POS.Services.IService;

namespace POS.API.Adapter;

/// <summary>
/// SignalR-backed implementation of <see cref="IBridgeNotifier"/>. Lives in
/// <c>POS.API</c> because <c>BridgeHub</c> and the typed
/// <see cref="IHubContext{THub, T}"/> are only resolvable inside the web
/// host — keeps <c>POS.Services</c> free of any reference to <c>POS.API</c>.
/// <para>
/// Per BDD-022 P4, this adapter consumes the strongly-typed
/// <see cref="IHubContext{BridgeHub, IBridgeClient}"/> so that every method
/// invocation on <c>Clients.Group(...).Method(...)</c> is compile-time bound
/// to <see cref="IBridgeClient"/>. The public <see cref="IBridgeNotifier"/>
/// interface in <c>POS.Services</c> is unchanged (BDD-022 D5).
/// </para>
/// </summary>
public class BridgeNotifier : IBridgeNotifier
{
    private readonly IHubContext<BridgeHub, IBridgeClient> _hub;

    public BridgeNotifier(IHubContext<BridgeHub, IBridgeClient> hub)
    {
        _hub = hub;
    }

    /// <inheritdoc />
    public Task NotifyAccessGrantedAsync(int branchId, int customerId)
        => _hub.Clients
            .Group(BridgeHub.BuildHardwareGroupName(branchId))
            .OpenTurnstile(customerId);

    /// <inheritdoc />
    public Task NotifyAccessAttemptAsync(int branchId, AccessResultDto result)
        => _hub.Clients
            .Group(BridgeHub.BuildDashboardGroupName(branchId))
            .AccessAttempted(result);

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
            .SendEscPosCommand(payload);
    }
}
