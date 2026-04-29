namespace POS.Domain.Models;

/// <summary>
/// Line-item child of <see cref="Subscription"/> backing Stripe's multi-item
/// model (Plan base + N add-ons). Each row maps 1:1 to a row in
/// <c>stripe_subscription.items.data</c>.
/// <para>
/// <see cref="IsBasePlan"/> defaults to <c>false</c> (fail-closed): the
/// Stripe webhook handler must explicitly mark the item base when its
/// <see cref="StripePriceId"/> is recognized in <c>StripeConstants.PriceMap</c>.
/// Forgetting to set it leaves the row classified as an add-on, which is the
/// safer failure mode — it prevents accidental promotion that would silently
/// downgrade the tenant's <c>Business.PlanTypeId</c>.
/// </para>
/// </summary>
public class SubscriptionItem
{
    public int Id { get; set; }

    public int SubscriptionId { get; set; }
    public Subscription Subscription { get; set; } = null!;

    /// <summary>
    /// The specific item ID assigned by Stripe (<c>si_XXX</c>). Required for
    /// future quantity updates because Stripe's API targets the item, not the
    /// price. A unique index in the DB enforces idempotency on webhook replay.
    /// </summary>
    public string StripeItemId { get; set; } = null!;

    public string StripePriceId { get; set; } = null!;

    public int Quantity { get; set; } = 1;

    public bool IsBasePlan { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
