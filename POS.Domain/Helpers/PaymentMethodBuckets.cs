using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// The four coarse buckets the admin dashboard summary reports payments under.
/// </summary>
public enum PaymentBucket
{
    Cash,
    Card,
    Transfer,
    Other
}

/// <summary>
/// Maps each <see cref="PaymentMethod"/> to its dashboard summary bucket. This is
/// the in-code precursor to the data-driven PaymentMethodCatalog category planned
/// for a later refactor — keep the two in sync when that lands.
/// </summary>
public static class PaymentMethodBuckets
{
    public static PaymentBucket BucketOf(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => PaymentBucket.Cash,

        // Card-present rails (physical card + card-backed terminals).
        PaymentMethod.Card => PaymentBucket.Card,
        PaymentMethod.Clip => PaymentBucket.Card,
        PaymentMethod.BankTerminal => PaymentBucket.Card,

        PaymentMethod.Transfer => PaymentBucket.Transfer,

        // MercadoPago (wallet/QR), store credit, loyalty points and the catch-all
        // fall to Other for now; refined per-method once the catalog is editable.
        _ => PaymentBucket.Other
    };
}
