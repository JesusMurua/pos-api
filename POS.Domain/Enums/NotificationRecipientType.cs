namespace POS.Domain.Enums;

/// <summary>
/// Who a notification is addressed to. The concrete email is resolved at ENQUEUE time
/// (not at send time) so it captures the address valid when the trigger fired:
/// <list type="bullet">
/// <item><see cref="Owner"/> — the business owner user's email.</item>
/// <item><see cref="BillingEmail"/> — <c>Subscription.BillingEmail</c>, falling back to the owner.</item>
/// <item><see cref="Custom"/> — an explicit address supplied by the caller.</item>
/// </list>
/// </summary>
public enum NotificationRecipientType
{
    Owner,
    BillingEmail,
    Custom
}
