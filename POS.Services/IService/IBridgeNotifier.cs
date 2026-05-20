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

    /// <summary>
    /// Sends a raw ESC/POS byte stream to a specific thermal printer attached
    /// to the local hardware bridge. Bytes are base64-encoded inside an
    /// <c>EscPosPayloadDto</c> so the SignalR JSON protocol carries them
    /// natively. Fire-and-forget — without a persistent outbox the message
    /// is lost if no bridge is connected at the moment of dispatch.
    /// </summary>
    Task SendEscPosCommandAsync(int branchId, string printerId, byte[] escPosBytes);
}
