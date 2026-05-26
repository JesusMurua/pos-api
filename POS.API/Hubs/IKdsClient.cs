namespace POS.API.Hubs;

/// <summary>
/// Strongly-typed contract for every server → client method emitted on
/// <see cref="KdsHub"/>. As of BDD-022 v1 the surface is intentionally
/// small (one method); additional KDS events would be added here so the
/// dispatcher worker stays compile-time bound to the contract.
/// <para>
/// Per BDD-022 §4.1, hub typing enforces <strong>payload</strong> shape
/// only — group routing
/// (<c>branch-{branchId}-{destination}</c>) remains the imperative
/// responsibility of the call site (see
/// <c>KdsEventDispatcherWorker</c>).
/// </para>
/// </summary>
public interface IKdsClient
{
    /// <summary>
    /// Notifies every KDS terminal subscribed to a given branch+destination
    /// group that a new print job has been generated. Emitted by
    /// <c>KdsEventDispatcherWorker</c> when it drains a row from the
    /// <c>KdsEventOutbox</c> table; at-least-once delivery because the
    /// outbox row is only marked <c>IsProcessed = true</c> after the
    /// broadcast succeeds.
    /// <para>
    /// The trailing <see cref="CancellationToken"/> is consumed by the
    /// SignalR server-side <c>SendAsync</c> machinery to abort the
    /// outbound dispatch on host shutdown; it is NOT transmitted to the
    /// connected clients (standard typed-hub convention).
    /// </para>
    /// </summary>
    /// <param name="payload">
    /// Serialized print-job payload carried verbatim from
    /// <c>KdsEventOutbox.Payload</c>. Currently a JSON string; a future
    /// BDD may tighten this to a typed DTO.
    /// </param>
    /// <param name="cancellationToken">Aborts the server-side send on shutdown.</param>
    Task PrintJobCreated(string payload, CancellationToken cancellationToken = default);
}
