using POS.Domain.DTOs.AccessControl;

namespace POS.Services.IService;

/// <summary>
/// Abstraction over the SignalR transport that pushes commands from the
/// backend to the local hardware bridge in real time. Exposed as an interface
/// in <c>POS.Services</c> so that <c>POS.Services</c> never takes a project
/// reference to <c>POS.API</c> (which would create a dependency cycle).
/// The concrete implementation lives in <c>POS.API/Adapter/BridgeNotifier.cs</c>
/// and wraps <c>IHubContext&lt;BridgeHub&gt;</c>.
/// </summary>
public interface IBridgeNotifier
{
    /// <summary>
    /// Tells every bridge connected to <paramref name="branchId"/> to open the
    /// turnstile for <paramref name="customerId"/>. Fire-and-forget at the
    /// SignalR layer — the bridge does not ack back over the same channel.
    /// </summary>
    Task NotifyAccessGrantedAsync(int branchId, int customerId);

    /// <summary>
    /// Broadcasts every access attempt (granted, denied, unknown QR) to the
    /// branch's SignalR group so the admin dashboard receives a live feed
    /// alongside the hardware-only <c>OpenTurnstile</c> command.
    /// </summary>
    Task NotifyAccessAttemptAsync(int branchId, AccessResultDto result);
}
