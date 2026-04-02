namespace POS.Domain.Helpers;

/// <summary>
/// Constants for Stripe subscription status values. Eliminates magic strings across the codebase.
/// </summary>
public static class StripeSubscriptionStatus
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";
    public const string Paused = "paused";
    public const string Incomplete = "incomplete";
    public const string IncompleteExpired = "incomplete_expired";
    public const string Unpaid = "unpaid";
}
