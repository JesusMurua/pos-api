namespace POS.Domain.Enums;

/// <summary>
/// Lifecycle states for <see cref="POS.Domain.Models.CustomerMembership"/>.
/// Persisted as a string column via <c>HasConversion&lt;string&gt;()</c> so the
/// database value is human-readable in queries and audit reports.
/// </summary>
public enum MembershipStatus
{
    /// <summary>The membership is current and grants access until <c>ValidUntil</c>.</summary>
    Active,

    /// <summary>The membership reached its <c>ValidUntil</c> without renewal.</summary>
    Expired,

    /// <summary>
    /// The membership clock is paused (e.g. medical leave, suspended account).
    /// Frozen memberships block both access and extensions until manually un-frozen.
    /// </summary>
    Frozen,

    /// <summary>The membership was explicitly cancelled (e.g. refund, account closure).</summary>
    Cancelled
}
