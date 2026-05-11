using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Domain.Extensions;

/// <summary>
/// Domain helpers for evaluating <see cref="CustomerMembership"/> state.
/// Single source of truth for the "currently active" predicate so callers
/// (access control, dashboards, billing jobs) cannot drift apart.
/// </summary>
public static class CustomerMembershipExtensions
{
    /// <summary>
    /// Returns <c>true</c> when the membership row is stored as
    /// <see cref="MembershipStatus.Active"/> AND its <c>ValidUntil</c> has not
    /// elapsed at <paramref name="now"/>. Mirrors the lazy-Expired projection
    /// applied by <c>CustomerMembershipRepository</c>: rows with
    /// <c>Status = Active</c> but past <c>ValidUntil</c> are logically expired
    /// even though the DB column has not been rewritten yet.
    /// </summary>
    /// <remarks>
    /// IN-MEMORY USE ONLY. Do not pass this method into an EF Core LINQ query —
    /// the expression tree cannot be translated to SQL. Inline the equivalent
    /// predicate (<c>m.Status == MembershipStatus.Active &amp;&amp; m.ValidUntil &gt;= now</c>)
    /// when filtering at the database boundary.
    /// </remarks>
    public static bool IsCurrentlyActive(this CustomerMembership m, DateTime now)
        => m.Status == MembershipStatus.Active && m.ValidUntil >= now;
}
