namespace POS.Domain.Enums;

public enum PaymentMethod
{
    Cash,
    Card,
    Transfer,
    Other,
    /// <summary>Payment processed via Clip physical terminal.</summary>
    Clip,
    /// <summary>Payment processed via MercadoPago QR or checkout.</summary>
    MercadoPago,
    /// <summary>Payment processed via a generic bank terminal (non-Clip).</summary>
    BankTerminal,
    /// <summary>Payment using store credit (fiado / saldo a favor).</summary>
    StoreCredit,
    /// <summary>Payment using loyalty points redeemed as currency.</summary>
    LoyaltyPoints
}
