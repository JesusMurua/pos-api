namespace POS.Domain.Enums;

/// <summary>
/// The explicit admin action recorded in <see cref="Models.BusinessAuditLog"/>.
/// Distinct from the transparent per-field <c>AuditInterceptor</c> trail: this is
/// the operator-intent log (who did what to a tenant, and why). Stored as a string
/// (<c>HasConversion&lt;string&gt;</c>) so the wire/DB value is the stable name.
/// </summary>
public enum BusinessAuditAction
{
    Created,
    Suspended,
    Reactivated,
    PlanChanged,
    TrialExtended,
    PasswordReset,
    Impersonated,

    // Reserved for later SaaS-billing PRs (declared now so the enum/string mapping
    // is stable and no migration is needed when they start being written).
    SubscriptionCreated,
    SubscriptionPriceChanged,
    AddOnActivated,
    AddOnDeactivated,
    InvoiceCreated,
    InvoiceVoided,
    PaymentRegistered,
    PaymentDeleted,
    CfdiToggled,
    NotificationSent,
    Other
}
