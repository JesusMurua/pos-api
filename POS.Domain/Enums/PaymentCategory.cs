namespace POS.Domain.Enums;

/// <summary>
/// Behavioral category of a payment method. Unlike <see cref="PaymentMethod"/>
/// (the concrete, catalog-driven instrument), the category is a fixed primitive
/// that drives behavior: overpay/change eligibility, customer/reference gating,
/// and the report bucket a payment rolls up into. Frozen onto each OrderPayment
/// at sale time so historical reports never drift if the catalog is edited.
/// </summary>
public enum PaymentCategory
{
    /// <summary>Physical cash — the only category that produces change.</summary>
    Cash,

    /// <summary>Card-present rails: physical card and card-backed terminals.</summary>
    Card,

    /// <summary>Bank transfers, wallets and QR without an underlying card.</summary>
    Digital,

    /// <summary>Customer store credit — requires a customer, consumes balance.</summary>
    Credit,

    /// <summary>Loyalty points redeemed as currency — requires a customer.</summary>
    Points,

    /// <summary>Vouchers / gift codes validated by a code.</summary>
    Voucher,

    /// <summary>Catch-all for anything that does not fit the above.</summary>
    Other
}
