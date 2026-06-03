namespace POS.Domain.DTOs.Pos;

/// <summary>
/// Discrete result of the
/// <c>POST /api/Pos/initialize-cashier-session</c> orchestration. Surfaced
/// in the response so the SPA can render the appropriate toast and so the
/// audit log line carries a typed event without parsing free-form strings.
/// Serialized to camelCase via the global <c>JsonStringEnumConverter</c>.
/// </summary>
public enum InitializeOutcome
{
    /// <summary>Register did not exist and was inserted clean.</summary>
    Created,

    /// <summary>Register existed without a bound device; assigned silently.</summary>
    LinkedOrphan,

    /// <summary>
    /// Register existed with a different bound device but no open session;
    /// device was reassigned without operator confirmation.
    /// </summary>
    Reassigned,

    /// <summary>
    /// Register already pointed at the requesting device; no mutation
    /// occurred. Lets the SPA short-circuit "you are already linked" UX.
    /// </summary>
    Idempotent,

    /// <summary>
    /// Register existed with a different bound device that had an open
    /// session, and the operator confirmed the takeover via <c>Force=true</c>.
    /// The previous session is force-closed inside the same transaction.
    /// </summary>
    ForceTakeover
}
