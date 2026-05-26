using POS.Domain.DTOs.AccessControl;
using POS.Domain.DTOs.Bridge;

namespace POS.API.Hubs;

/// <summary>
/// Strongly-typed contract for every server → client method emitted on
/// <see cref="BridgeHub"/>. Drives the compile-time payload safety guarantee
/// established by BDD-022: a typo in any method name or argument type on
/// the backend dispatch sites (the <c>BridgeNotifier</c> adapter or
/// hub-internal emissions) becomes a build error instead of a silent
/// production regression on the local hardware bridge.
/// <para>
/// IMPORTANT: hub typing enforces <strong>payload</strong> shape only — NOT
/// group routing. The interface lists every method emitted by the hub
/// regardless of whether it lands on the hardware group
/// (<c>bridge-hardware-{branchId}</c>) or the dashboard group
/// (<c>bridge-dashboard-{branchId}</c>). Routing is the imperative
/// responsibility of the call site (see <see cref="BridgeHub"/>
/// group-name helpers and <c>BridgeNotifier</c>). See BDD-022 §4.1.
/// </para>
/// </summary>
public interface IBridgeClient
{
    /// <summary>
    /// Pushes the offline access cache (active memberships + QR hashes) to
    /// a freshly-connected bridge so it can authorize scans locally without
    /// round-tripping the cloud. Emitted by <c>BridgeHub.OnConnectedAsync</c>
    /// only when the connecting device carries <c>mode=bridge</c>; targeted
    /// at <c>Clients.Caller</c> (the connecting bridge itself, never echoed
    /// to the group).
    /// </summary>
    /// <param name="records">Active membership snapshot — see BDD-022 §5.2.1.</param>
    Task SyncAccessData(IReadOnlyList<SyncAccessRecordDto> records);

    /// <summary>
    /// Commands the local hardware bridge to open the turnstile for the
    /// given customer. Fire-and-forget at the SignalR layer: the bridge
    /// does not ack back over the same channel. Emitted to the
    /// <c>bridge-hardware-{branchId}</c> group only on granted access.
    /// </summary>
    /// <param name="customerId">Customer whose access was just authorized.</param>
    Task OpenTurnstile(int customerId);

    /// <summary>
    /// Sends a raw ESC/POS byte stream (base64-encoded inside an
    /// <see cref="EscPosPayloadDto"/>) to a specific thermal printer
    /// attached to the local bridge. Emitted to the
    /// <c>bridge-hardware-{branchId}</c> group.
    /// <para>
    /// Fire-and-forget warning (carried over from <c>BridgeNotifier</c>):
    /// without a persistent outbox, the message is lost if no bridge is
    /// connected at the moment of dispatch. A PrintJobOutbox migration is
    /// tracked as a future BDD.
    /// </para>
    /// </summary>
    /// <param name="payload">Printer identifier plus base64 ESC/POS bytes.</param>
    Task SendEscPosCommand(EscPosPayloadDto payload);

    /// <summary>
    /// Broadcasts every access attempt (granted, denied, unknown QR) to
    /// the branch's <c>bridge-dashboard-{branchId}</c> group so admin
    /// dashboards receive a live feed alongside the hardware-only
    /// <see cref="OpenTurnstile"/> command.
    /// </summary>
    /// <param name="result">Resolved access outcome for the scan attempt.</param>
    Task AccessAttempted(AccessResultDto result);

    /// <summary>
    /// Forwards a live scale reading from the bridge to the
    /// <c>bridge-dashboard-{branchId}</c> group only. The bridge does not
    /// receive its own broadcast back (no echo). Emitted from the
    /// <c>BridgeHub.ProcessWeightRead</c> inbound handler.
    /// </summary>
    /// <param name="payload">Scale reading payload (weight + unit + scale identifier).</param>
    Task OnWeightUpdated(WeightPayloadDto payload);
}
